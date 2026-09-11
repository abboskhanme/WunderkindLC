using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// KPI KIRISH RAQAMLARINI TIZIMDAN YIG'ISH — "qayerdan" izi bilan.
///
/// <para>Bu sinf modulning YURAGI: <see cref="KpiCalculator"/> sof formulani biladi, bu yerda esa
/// o'sha formulaga tushadigan HAR BIR RAQAM tizimning qaysi jadvalidan, qaysi shart bilan
/// olingani hal qilinadi. Har raqam <see cref="KpiInputDto"/> bo'lib chiqadi va o'zi bilan
/// <c>Source</c> (qanday sanaldi) hamda <c>Link</c> (isbot sahifasi) olib yuradi — xodim bosib
/// tekshira olsin. Izsiz raqam bahsni hal qilmaydi.</para>
///
/// <para><b>YUKLASH USULI — OMMAVIY (`RetentionBonusService` naqshi).</b> Hamma narsa
/// <c>AsNoTracking()</c> bilan BIR MARTA yuklanadi va xotirada birlashtiriladi. Xodim boshiga
/// yoki o'quvchi boshiga alohida so'rov (N+1) YO'Q: «Yopish» sahifasi bir necha xodimni birdan
/// hisoblaydi, chiquvchi adminning raqamlari esa butun bazani (minglab o'quvchi) qamraydi.</para>
///
/// <para><b>HECH NARSA SAQLANMAYDI.</b> Oylik hisob har so'rovda qayta hisoblanadi — kechikkan
/// ma'lumot (sinov natijasi, ketish sababi, to'lov) kiritilsa katak o'z-o'zidan tuzaladi.
/// Yagona istisno — TASDIQLANGAN oy: u <c>KpiMonthResult</c> dan MUZLATILGAN holda qaytadi
/// (<see cref="MonthAsync"/>), aks holda yopishning ma'nosi qolmasdi.</para>
///
/// <para>⚠️ Servis SAQLAMAYDI (loyiha qoidasi): <c>SaveChangesAsync</c> ni controller chaqiradi.</para>
/// </summary>
public static class KpiMetricsService
{
    /* =========================================================================================
     *  ISBOT SAHIFALARI — raqamning "qayerdan" havolasi
     *  ⚠️ Bular MAVJUD marshrutlar (`App.tsx`). Yangi havola qo'shsangiz avval marshrut borligini
     *  tekshiring: bo'lmagan manzil xodimni bo'sh sahifaga olib borardi va "raqam yolg'on"
     *  taassurotini berardi.
     * ====================================================================================== */

    private const string LinkLeads = "/admin/leads";
    private const string LinkStudents = "/admin/students";
    private const string LinkAttendance = "/admin/students/davomat";
    private const string LinkFinance = "/admin/finance";
    private const string LinkClasses = "/admin/classes";
    private const string LinkTasks = "/admin/topshiriqlar";
    private const string LinkCalls = "/admin/calls";
    private const string LinkTickets = "/admin/boshqaruv/kpi/tiketlar";
    private const string LinkToday = "/admin/boshqaruv/kpi/bugun";
    private const string LinkHistory = "/admin/settings/history";

    /// <summary>Snapshot JSON kaliti — oy boshidagi FAOL o'quvchilar soni.</summary>
    public const string SnapActiveCount = "activeCount";
    /// <summary>Snapshot JSON kaliti — oy boshidagi umumiy QARZ (so'm).</summary>
    public const string SnapDebtTotal = "debtTotal";

    /// <summary>Bir haftada nechta kun (ketma-ket kelmaslik ro'yxatlari uchun oyna).</summary>
    private const int AlertLookbackDays = 45;

    /// <summary>«Kursi tugayapti» ro'yxatining oynasi (spetsifikatsiya: 3 hafta).</summary>
    private const int EndingSoonDays = 21;

    /* =========================================================================================
     *  OMMAVIY YO'L
     * ====================================================================================== */

    /// <summary>
    /// Bitta xodimning bitta oydagi hisobi.
    /// </summary>
    /// <remarks>
    /// ⚠️ Oy YOPILGAN bo'lsa (<c>KpiMonthResult.Status == "confirmed"</c>) MUZLATILGAN qiymatlar
    /// qaytadi, jonli hisob EMAS. Aks holda yopishdan keyin qoida yoki lid tahrirlansa xodimga
    /// aytilgan summa jimgina o'zgarib ketardi — modulning butun ishonchi shunga bog'liq.
    /// </remarks>
    public static async Task<KpiMonthDto> MonthAsync(
        IAppDbContext db, string userId, string month, CancellationToken ct = default)
    {
        var ctx = await LoadMonthAsync(db, month, userId, ct);
        return Build(ctx, userId);
    }

    /// <summary>
    /// Oyning BARCHA KPI xodimlari bo'yicha jadval («Oy» va «Yopish» sahifalari).
    /// </summary>
    /// <remarks>⚠️ Ma'lumot bir marta yuklanadi va hamma xodim uchun xotirada hisoblanadi:
    /// har xodim uchun <see cref="MonthAsync"/> chaqirilsa butun baza N marta o'qilardi.</remarks>
    public static async Task<List<KpiMonthDto>> MonthAllAsync(
        IAppDbContext db, string month, CancellationToken ct = default)
    {
        var ctx = await LoadMonthAsync(db, month, null, ct);
        return ctx.Profiles
            .OrderBy(p => p.RoleCode, StringComparer.Ordinal)
            .ThenBy(p => ctx.UserName(p.UserId), StringComparer.CurrentCulture)
            .Select(p => Build(ctx, p.UserId))
            .ToList();
    }

    /// <summary>
    /// «Bugun» sahifasi: kunlik norma (reja/fakt), cheklist, signallar va (chiquvchi uchun) erta
    /// ogohlantirish ro'yxatlari.
    /// </summary>
    public static async Task<KpiTodayDto> TodayAsync(
        IAppDbContext db, string userId, string date, CancellationToken ct = default)
    {
        var day = ParseDate(date) ?? AppClock.Today;
        var dayIso = day.ToString("yyyy-MM-dd");
        var month = dayIso[..7];

        var profile = await db.KpiProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var role = profile?.RoleCode ?? KpiConst.Intake;
        var userName = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "";

        var rules = await RulesForAsync(db, role, month, ct) ?? KpiRuleSeed.For(role);

        var norms = role == KpiConst.Retention
            ? await RetentionNormsAsync(db, dayIso, ct)
            : await IntakeNormsAsync(db, userId, dayIso, rules, ct);

        var blocks = await ChecklistBlocksAsync(db, userId, role, dayIso, ct);
        var signals = await SignalsAsync(db, userId, role, dayIso, month, ct);
        var lists = role == KpiConst.Retention
            ? await RetentionAlertListsAsync(db, dayIso, ct)
            : [];

        // PROGNOZ: oyning shu kunigacha bo'lgan hisobi + "shu tezlikda davom etsa" ulushi.
        // ⚠️ Summani kunlar nisbatida CHO'ZIB ko'rsatmaymiz (masalan ×30/10): oklad oyning
        // boshida ham to'liq turadi, ya'ni chiziqli ko'paytirish soxta katta son berardi.
        // Progress esa oy qanchalik o'tganini ko'rsatadi — xodim o'zi taqqoslaydi.
        KpiMonthForecastDto? forecast = null;
        if (profile is not null)
        {
            var live = await MonthAsync(db, userId, month, ct);
            var daysInMonth = DateTime.DaysInMonth(day.Year, day.Month);
            var progress = daysInMonth == 0 ? 0 : (double)day.Day / daysInMonth;
            forecast = new KpiMonthForecastDto(
                live.Salary, progress,
                $"Oyning {Math.Round(progress * 100)}% i o'tdi. Hozirgi hisob: {Money(live.Salary)} so'm.");
        }

        return new KpiTodayDto(userId, userName, role, dayIso, norms, blocks, signals, lists, forecast);
    }

    /// <summary>
    /// BIRLIK IQTISODIYOTI — «Yopish» sahifasidagi uchta sog'lomlik indikatori:
    /// bonus A marjaning 3% idan, bonus B 5% idan, barcha KPI oyliklari markaz marjasining
    /// 10% idan oshmaydimi.
    /// </summary>
    /// <remarks>
    /// ⚠️ Narx va o'qituvchi ulushi HAQIQIY guruhlardan olinadi (<c>Group.MonthlyFee</c>,
    /// <c>Group.TeacherSalaryPercent</c>) — qoidalardagi konstanta faqat ZAXIRA. Sabab:
    /// konstanta bir marta kiritilib unutiladi, guruh narxlari esa o'zgarib turadi va indikator
    /// eskirgan narx bilan "hammasi joyida" deb turaverardi.
    ///
    /// ⚠️ NARXI: bu chaqiruv o'quvchilar bazasini IKKI marta o'qiydi (faol o'quvchilar sanog'i +
    /// <see cref="MonthAllAsync"/> ichidagi chiquvchi hisobi). ATAYIN: «Yopish» — oyda bir-ikki
    /// marta ochiladigan rahbar sahifasi, hisobni esa ikki manbadan yig'ib "taxminan" qilishdan
    /// ko'ra ikki marta to'g'ri o'qigan ma'qul. Sahifa sezilarli sekinlashsa — kontekstni
    /// tashqaridan uzatadigan overload qo'shing, sanoq usulini O'ZGARTIRMANG.
    /// </remarks>
    public static async Task<KpiUnitEconomicsDto> UnitEconomicsAsync(
        IAppDbContext db, string month, CancellationToken ct = default)
    {
        var win = Window(month);

        // Konstantalar — chiquvchi rolining qoidalaridan (birlik iqtisodiyoti MARKAZ darajasidagi
        // qiymat, ya'ni har uchala seed'da bir xil); topilmasa kiruvchiniki, u ham bo'lmasa seed.
        var rules = await RulesForAsync(db, KpiConst.Retention, month, ct)
                    ?? await RulesForAsync(db, KpiConst.Intake, month, ct)
                    ?? KpiRuleSeed.Retention();

        // FAOL guruhlar: arxivlanmagan va yopilmagan. Narxsiz (0) guruhlar o'rtachani pastga
        // tortib yuborardi — ular hisobga kirmaydi.
        var groups = await db.Classes.AsNoTracking()
            .Where(g => !g.IsArchived && g.Status == "active")
            .Select(g => new { g.MonthlyFee, g.TeacherSalaryPercent })
            .ToListAsync(ct);

        var priced = groups.Where(g => g.MonthlyFee > 0).ToList();
        var coursePrice = priced.Count > 0
            ? KpiCalculator.Som(priced.Average(g => g.MonthlyFee))
            : rules.UnitCoursePrice;

        var shared = groups.Where(g => g.TeacherSalaryPercent > 0).ToList();
        var teacherShare = shared.Count > 0
            ? (double)shared.Average(g => g.TeacherSalaryPercent) / 100.0
            : rules.UnitTeacherShare;
        // Foiz 0..1 oralig'idan chiqib ketsa (qo'lda 155 kiritilgan) marja MANFIY bo'lib,
        // butun indikator ma'nosini yo'qotardi.
        teacherShare = Math.Clamp(teacherShare, 0, 0.95);

        var marginPerStudent = KpiCalculator.Som(coursePrice * (1m - (decimal)teacherShare));

        var students = await CenterActiveCountAsync(db, win.AsOfEndIso, ct);
        var centerMargin = KpiCalculator.Som(marginPerStudent * students);

        var bonusAUnit = rules.ActiveStudentBonus;
        var bonusAShare = marginPerStudent == 0 ? 0 : (double)(bonusAUnit / marginPerStudent);

        var months = Math.Max(1, rules.UnitExtensionMonths);
        var extensionMargin = KpiCalculator.Som(marginPerStudent * months);
        var bonusBUnit = rules.ExtensionBonus;
        var bonusBShare = extensionMargin == 0 ? 0 : (double)(bonusBUnit / extensionMargin);

        var rows = await MonthAllAsync(db, month, ct);
        var totalSalaries = rows.Sum(r => r.Salary);
        var salaryShare = centerMargin == 0 ? 0 : (double)(totalSalaries / centerMargin);

        return new KpiUnitEconomicsDto(
            coursePrice, teacherShare, marginPerStudent, students, centerMargin,
            bonusAUnit, bonusAShare, rules.UnitMaxBonusAShare, bonusAShare <= rules.UnitMaxBonusAShare,
            bonusBUnit, extensionMargin, bonusBShare, rules.UnitMaxBonusBShare, bonusBShare <= rules.UnitMaxBonusBShare,
            totalSalaries, salaryShare, rules.UnitMaxSalaryShare, salaryShare <= rules.UnitMaxSalaryShare);
    }

    /* =========================================================================================
     *  OY KONTEKSTI — hamma narsa BIR MARTA yuklanadi
     * ====================================================================================== */

    /// <summary>Oy chegaralari va "qaysi sanaga qarab hisoblaymiz" javobi.</summary>
    /// <param name="AsOfEnd">Oy oxiri YOKI bugun — qaysi biri OLDIN kelsa. JORIY oy uchun
    /// "oy oxiridagi holat" hali kelmagan, kelajakdagi sanaga qarab hisoblash esa
    /// (masalan hali yozilmagan hisoblarni "to'lanmagan" deb) yolg'on natija berardi.</param>
    private sealed record MonthWindow(
        string Month, DateOnly First, DateOnly Last, DateOnly AsOfEnd,
        string FirstIso, string LastIso, string AsOfEndIso, string PrevMonth);

    private static MonthWindow Window(string month)
    {
        var m = month is { Length: >= 7 } ? month[..7] : AppClock.Today.ToString("yyyy-MM");
        if (!DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var first))
        {
            // Buzuq oy ("2026-13") — joriy oyga tushiriladi. Xatoga yiqilsak xodim o'z
            // oyligini umuman ko'rmasdi.
            m = AppClock.Today.ToString("yyyy-MM");
            first = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1);
        }
        var last = first.AddMonths(1).AddDays(-1);
        var asOf = last > AppClock.Today ? AppClock.Today : last;
        if (asOf < first) asOf = first;      // kelajakdagi oy so'ralsa — oy boshi holati.
        return new MonthWindow(
            m, first, last, asOf,
            first.ToString("yyyy-MM-dd"), last.ToString("yyyy-MM-dd"), asOf.ToString("yyyy-MM-dd"),
            first.AddMonths(-1).ToString("yyyy-MM"));
    }

    /// <summary>Bitta oydagi barcha xodimlar uchun kerak bo'ladigan MA'LUMOT (bir marta yuklangan).</summary>
    private sealed class MonthCtx
    {
        public required MonthWindow Win { get; init; }
        public required List<KpiProfile> Profiles { get; init; }
        public required Dictionary<string, string> Names { get; init; }
        public required List<KpiRuleSet> Rules { get; init; }
        public required List<KpiProfileSalary> Salaries { get; init; }
        public required Dictionary<string, KpiMonthResult> Results { get; init; }
        public required Dictionary<string, Dictionary<string, double>> Snapshots { get; init; }

        /// <summary>Kiruvchi rolining raqamlari (xodim → qiymat).</summary>
        public required Dictionary<string, IntakeFacts> Intake { get; init; }
        /// <summary>Chiquvchi rolining raqamlari — MARKAZ bo'yicha bitta to'plam.</summary>
        public CenterFacts? Center { get; init; }
        /// <summary>Samaradorlik va tiketlar — har rol uchun kerak.</summary>
        public required Dictionary<string, EffortFacts> Effort { get; init; }

        public string UserName(string userId) => Names.GetValueOrDefault(userId, "");
    }

    /// <summary>Kiruvchi adminning oylik xom raqamlari.</summary>
    private sealed record IntakeFacts(int Leads, int Unassigned, int TrialCame, int Contracts);

    /// <summary>Samaradorlik va tiketlar (ikkala rol uchun bir xil manba).</summary>
    /// <param name="ChecklistDone">Cheklistda "bajarildi" belgilari.</param>
    /// <param name="ChecklistTotal">"bajarildi" + "bajarilmadi" (⚠️ "na" MAXRAJGA kirmaydi).</param>
    /// <param name="TasksOnTime">Muddatida yopilgan topshiriqlar.</param>
    /// <param name="TasksDue">Muddati kelgan topshiriqlar.</param>
    private sealed record EffortFacts(
        int ChecklistDone, int ChecklistTotal, int TasksOnTime, int TasksDue, int Tickets);

    /// <summary>Chiquvchi adminning oylik xom raqamlari (markaz bo'yicha).</summary>
    private sealed record CenterFacts(
        int ActiveStart, int ActiveEnd, int LeftControlled, int UnknownReasonLeft,
        int CourseFinished, int Extended, decimal DebtStart, decimal DebtCollected,
        bool SnapshotMissing);

    private static async Task<MonthCtx> LoadMonthAsync(
        IAppDbContext db, string month, string? onlyUserId, CancellationToken ct)
    {
        var win = Window(month);

        var profQuery = db.KpiProfiles.AsNoTracking().Where(p => p.IsActive);
        if (onlyUserId is not null) profQuery = db.KpiProfiles.AsNoTracking().Where(p => p.UserId == onlyUserId);
        var profiles = await profQuery.ToListAsync(ct);

        var userIds = profiles.Select(p => p.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var rules = await db.KpiRuleSets.AsNoTracking().ToListAsync(ct);
        var salaries = await db.KpiProfileSalaries.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId)).ToListAsync(ct);

        var results = await db.KpiMonthResults.AsNoTracking()
            .Where(r => r.Month == win.Month && userIds.Contains(r.UserId))
            .ToListAsync(ct);

        var snapRows = await db.KpiMonthSnapshots.AsNoTracking()
            .Where(s => s.Month == win.Month).ToListAsync(ct);
        var snapshots = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (var s in snapRows) snapshots[SnapKey(s.RoleCode, s.UserId)] = ParseSnapshot(s.Json);

        var effort = await LoadEffortAsync(db, win, userIds, ct);

        var intakeUsers = profiles
            .Where(p => p.RoleCode != KpiConst.Retention)
            .Select(p => p.UserId).Distinct().ToList();
        var intake = intakeUsers.Count == 0
            ? new Dictionary<string, IntakeFacts>(StringComparer.Ordinal)
            : await LoadIntakeAsync(db, win, intakeUsers, ct);

        // ⚠️ Chiquvchi rolining raqamlari BUTUN bazani (o'quvchilar, jurnal, hisoblar, to'lovlar)
        // o'qishni talab qiladi — shuning uchun u FAQAT shu rol kerak bo'lganda yuklanadi.
        // Aks holda "Oy" sahifasini ochgan har bir kiruvchi admin butun bazani tortib olardi.
        var needCenter = profiles.Any(p => p.RoleCode == KpiConst.Retention);
        var center = needCenter ? await LoadCenterAsync(db, win, snapshots, ct) : null;

        return new MonthCtx
        {
            Win = win, Profiles = profiles, Names = names, Rules = rules, Salaries = salaries,
            Results = results.ToDictionary(r => r.UserId, StringComparer.Ordinal),
            Snapshots = snapshots, Intake = intake, Center = center, Effort = effort,
        };
    }

    private static string SnapKey(string role, string userId) => $"{role}|{userId}";

    private static Dictionary<string, double> ParseSnapshot(string json)
    {
        // Buzuq JSON (qo'lda tahrirlangan qator) butun sahifani yiqitmasin — bo'sh to'plam
        // qaytadi va hisob "snapshot yo'q" yo'liga tushadi.
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                   ?? new Dictionary<string, double>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }
    }

    /* =========================================================================================
     *  KIRUVCHI ADMIN — lid → sinov darsi → shartnoma
     * ====================================================================================== */

    /// <remarks>
    /// ⚠️ <b>BIRIKTIRILMAGAN LID.</b> Spetsifikatsiya §8: mas'ul maydoni bo'sh lidlar ham
    /// sanaladi va ularning soni DTO'da ALOHIDA qator bo'lib chiqadi. Sabab: maydon endi
    /// paydo bo'ldi va eski lidlarda bo'sh — faqat biriktirilganlarini sanasak, konversiya
    /// nolga tushib, butun modul "ishlamayapti" bo'lib ko'rinardi. ⚠️ Bunda IKKI kiruvchi admin
    /// bo'lsa biriktirilmagan lid IKKALASIGA ham sanaladi — shuning uchun soni ochiq
    /// ko'rsatiladi (yashirin emas) va lidlarni biriktirish odati shakllanishi kerak.
    ///
    /// ⚠️ <b>Sinovga KELGAN</b> ham AYNAN shu qoida bilan biriktiriladi (lidning mas'uli orqali):
    /// aks holda konversiyaning suratida bir to'plam, maxrajida boshqa to'plam turib,
    /// "lid → sinov" foizi ma'nosiz chiqardi.
    /// </remarks>
    private static async Task<Dictionary<string, IntakeFacts>> LoadIntakeAsync(
        IAppDbContext db, MonthWindow win, List<string> userIds, CancellationToken ct)
    {
        // Oy ichida YARATILGAN lidlar. `CreatedAt` ba'zan to'liq ISO ("...T09:00"), shuning uchun
        // "oyning birinchi kunidan" va "keyingi oyning birinchi kunigacha" oralig'i olinadi.
        var nextMonthIso = win.First.AddMonths(1).ToString("yyyy-MM-dd");
        var leads = await db.Leads.AsNoTracking()
            .Where(l => string.Compare(l.CreatedAt, win.FirstIso) >= 0
                        && string.Compare(l.CreatedAt, nextMonthIso) < 0)
            .Select(l => new { l.Id, l.AssigneeUserId })
            .ToListAsync(ct);

        // SHARTNOMA — lidni o'quvchiga aylantirgan admin (`LeadsController.Convert`).
        // Bu yerda biriktirilmagan yo'l YO'Q: shartnomani kim yopgani har doim yoziladi.
        var contracts = await db.Leads.AsNoTracking()
            .Where(l => l.ClosedByUserId != null && l.ClosedAt != null
                        && string.Compare(l.ClosedAt, win.FirstIso) >= 0
                        && string.Compare(l.ClosedAt, win.LastIso) <= 0)
            .Select(l => l.ClosedByUserId!)
            .ToListAsync(ct);

        // SINOVGA KELGAN: `AttendedAt` oy ichida, YOKI natijasi "kelgan" oilasidan va
        // rejalashtirilgan sanasi oy ichida (eski yozuvlarda `AttendedAt` bo'sh).
        string[] came = ["came", "stayed", "left"];
        var trials = await db.TrialLessons.AsNoTracking()
            .Where(t =>
                (t.AttendedAt != null
                 && string.Compare(t.AttendedAt, win.FirstIso) >= 0
                 && string.Compare(t.AttendedAt, win.LastIso) <= 0)
                || (came.Contains(t.Result)
                    && string.Compare(t.ScheduledAt, win.FirstIso) >= 0
                    && string.Compare(t.ScheduledAt, win.LastIso) <= 0))
            .Select(t => t.LeadId)
            .ToListAsync(ct);

        // Sinov darsining EGASI — lidning mas'uli. Sinov lidlari oyning oldingi oylarida
        // yaratilgan bo'lishi mumkin, shuning uchun alohida so'rov bilan olinadi.
        var trialLeadIds = trials.Distinct().ToList();
        var trialOwner = await db.Leads.AsNoTracking()
            .Where(l => trialLeadIds.Contains(l.Id))
            .Select(l => new { l.Id, l.AssigneeUserId })
            .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);

        var unassignedLeads = leads.Count(l => string.IsNullOrWhiteSpace(l.AssigneeUserId));
        var unassignedTrials = trials.Count(id =>
            string.IsNullOrWhiteSpace(trialOwner.GetValueOrDefault(id)));

        var result = new Dictionary<string, IntakeFacts>(StringComparer.Ordinal);
        foreach (var uid in userIds)
        {
            var mine = leads.Count(l => l.AssigneeUserId == uid);
            var mineTrials = trials.Count(id => trialOwner.GetValueOrDefault(id) == uid);
            result[uid] = new IntakeFacts(
                Leads: mine + unassignedLeads,
                Unassigned: unassignedLeads,
                TrialCame: mineTrials + unassignedTrials,
                Contracts: contracts.Count(c => c == uid));
        }
        return result;
    }

    /* =========================================================================================
     *  SAMARADORLIK va TIKETLAR
     * ====================================================================================== */

    /// <remarks>
    /// ⚠️ <b>SAMARADORLIK QAYERDAN.</b> Spetsifikatsiya ikki xil manba nomlaydi: §8 jadvali —
    /// topshiriqlar (<c>WorkTask</c>), <c>KpiRuleSetJson.Efficiency</c> va <c>KpiConst.CheckNa</c>
    /// izohlari esa KUNLIK CHEKLIST. Tanlov CHEKLIST foydasiga: <c>KpiConst.CheckNa</c> da
    /// «"na" MAXRAJGA ham kirmaydi» deb yozilgani aynan samaradorlik maxraji haqidagi qoida,
    /// ya'ni hisob cheklist ustiga qurilgan. Topshiriqlar ham o'lchanadi va ALOHIDA qator
    /// bo'lib chiqadi (yashirilmaydi).
    ///
    /// ⚠️ Cheklist belgilari UMUMAN bo'lmasa (modul endi yoqilgan, xodim hali belgilamagan)
    /// topshiriqlar ishlatiladi; ular ham bo'lmasa samaradorlik <b>1.0</b> bo'ladi va DTO'da
    /// OCHIQ ogohlantirish yoziladi. Nol qoldirsak, o'lchov hali yo'qligi uchun HAR BIR xodim
    /// eng past pog'onaga (0.75) tushib, oyligini yo'qotardi — o'lchanmagan narsa uchun jazo
    /// bo'lardi.
    /// </remarks>
    private static async Task<Dictionary<string, EffortFacts>> LoadEffortAsync(
        IAppDbContext db, MonthWindow win, List<string> userIds, CancellationToken ct)
    {
        var checks = await db.ChecklistEntries.AsNoTracking()
            .Where(e => userIds.Contains(e.UserId)
                        && string.Compare(e.Date, win.FirstIso) >= 0
                        && string.Compare(e.Date, win.LastIso) <= 0)
            .Select(e => new { e.UserId, e.State })
            .ToListAsync(ct);

        var tasks = await db.WorkTasks.AsNoTracking()
            .Where(t => userIds.Contains(t.AssigneeId) && !t.IsArchived && t.DueDate != null
                        && string.Compare(t.DueDate, win.FirstIso) >= 0
                        && string.Compare(t.DueDate, win.LastIso) <= 0)
            .Select(t => new { t.AssigneeId, t.DueDate, t.CompletedAt })
            .ToListAsync(ct);

        var tickets = await db.KpiTickets.AsNoTracking()
            .Where(t => userIds.Contains(t.UserId) && t.Status == KpiConst.TicketConfirmed
                        && string.Compare(t.Date, win.FirstIso) >= 0
                        && string.Compare(t.Date, win.LastIso) <= 0)
            .Select(t => t.UserId)
            .ToListAsync(ct);

        var todayIso = AppClock.Today.ToString("yyyy-MM-dd");
        var result = new Dictionary<string, EffortFacts>(StringComparer.Ordinal);
        foreach (var uid in userIds)
        {
            var mine = checks.Where(c => c.UserId == uid).ToList();
            var done = mine.Count(c => c.State == KpiConst.CheckDone);
            var failed = mine.Count(c => c.State == KpiConst.CheckFailed);

            // Muddati HALI kelmagan topshiriq maxrajga kirmaydi — aks holda oy o'rtasida
            // kelasi haftaning vazifalari "bajarilmagan" bo'lib sanalardi.
            var due = tasks.Where(t => t.AssigneeId == uid
                                       && string.CompareOrdinal(t.DueDate!, todayIso) <= 0).ToList();
            var onTime = due.Count(t => t.CompletedAt is DateTime c
                                        && string.CompareOrdinal(c.ToString("yyyy-MM-dd"), t.DueDate!) <= 0);

            result[uid] = new EffortFacts(done, done + failed, onTime, due.Count,
                tickets.Count(t => t == uid));
        }
        return result;
    }

    /* =========================================================================================
     *  CHIQUVCHI ADMIN — markaz bo'yicha baza, ketish, uzaytirish va qarz
     * ====================================================================================== */

    /// <summary>Bitta a'zolikning KPI uchun kerakli maydonlari.</summary>
    private sealed record MemberRow(string StudentId, string GroupId, string ActivatedAt,
        string? LeftAt, string FrozenAt, string Status, bool IsActive, List<string> PastPeriods);

    /// <summary>Bitta hisob qatori (chegirma ALOHIDA — 100% chegirma ham "hisob bor" degani).</summary>
    private sealed record ChargeRow(string StudentId, string Month, decimal Amount, decimal Discount, string Date);

    /// <summary>To'lov harakati — summa ISHORALI (vozvrat manfiy).</summary>
    private sealed record PayRow(string StudentId, string? Month, string Date, decimal Amount);

    /// <summary>O'quvchilar bazasining KPI uchun kerakli kesimi (bir marta yuklanadi).</summary>
    private sealed class StudentBase
    {
        public required HashSet<string> Archived { get; init; }
        public required Dictionary<string, List<MemberRow>> Members { get; init; }
        public required Dictionary<string, string> LastPresentAtStart { get; init; }
        public required Dictionary<string, string> LastPresentAtEnd { get; init; }
        public required Dictionary<string, List<ChargeRow>> Charges { get; init; }
        public required Dictionary<string, List<PayRow>> Pays { get; init; }
        public required List<string> StudentIds { get; init; }
        public required MonthWindow Win { get; init; }
    }

    private static async Task<StudentBase> LoadStudentBaseAsync(
        IAppDbContext db, MonthWindow win, CancellationToken ct)
    {
        var students = await db.Students.AsNoTracking()
            .Select(s => new { s.Id, s.IsArchived })
            .ToListAsync(ct);

        var members = (await db.StudentGroups.AsNoTracking()
                .Select(m => new MemberRow(m.StudentId, m.GroupId, m.ActivatedAt, m.LeftAt,
                    m.FrozenAt, m.Status, m.IsActive, m.PastPeriods))
                .ToListAsync(ct))
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // OXIRGI "kelgan" DARS — ikki sanaga (oy boshi va oy oxiri) alohida AGREGAT so'rov.
        // ⚠️ Butun jurnalni tortib olmaymiz: markazda u eng katta jadval. Guruhlash serverda
        // bajariladi, javob — o'quvchi boshiga bitta qator.
        var lastStart = await LastPresentAsync(db, win.FirstIso, ct);
        var lastEnd = await LastPresentAsync(db, win.AsOfEndIso, ct);

        var charges = (await db.MonthlyCharges.AsNoTracking()
                .Select(c => new ChargeRow(c.StudentId, c.Month, c.Amount, c.Discount, c.Date))
                .ToListAsync(ct))
            .GroupBy(c => c.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Month, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        // TO'LOVLAR: tuition tushumi PLYUS, vozvrat MINUS (`RetentionBonusService` bilan bir xil).
        var pays = (await db.FinanceTransactions.AsNoTracking()
                .Where(t => t.StudentId != null
                            && ((t.Direction == "income" && t.Category == "tuition")
                                || (t.Direction == "expense" && t.Category == "refund")))
                .Select(t => new { StudentId = t.StudentId!, t.Month, t.Date, t.Amount, t.Direction })
                .ToListAsync(ct))
            .Select(t => new PayRow(t.StudentId, t.Month, t.Date,
                t.Direction == "expense" ? -t.Amount : t.Amount))
            .GroupBy(p => p.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        return new StudentBase
        {
            Archived = students.Where(s => s.IsArchived).Select(s => s.Id).ToHashSet(StringComparer.Ordinal),
            Members = members,
            LastPresentAtStart = lastStart,
            LastPresentAtEnd = lastEnd,
            Charges = charges,
            Pays = pays,
            StudentIds = students.Select(s => s.Id).ToList(),
            Win = win,
        };
    }

    private static async Task<Dictionary<string, string>> LastPresentAsync(
        IAppDbContext db, string asOfIso, CancellationToken ct)
    {
        // ⚠️ `Max` LINQ tomonidan `string?` deb ko'riladi (bo'sh guruh nazariy jihatdan null
        // beradi), lekin guruh HAR DOIM kamida bitta qatordan tuziladi — shuning uchun bo'sh
        // qiymat ochiq tashlab yuboriladi, jimgina `null!` bilan yashirilmaydi.
        var rows = await db.JournalEntries.AsNoTracking()
            .Where(e => e.Present && string.Compare(e.Date, asOfIso) <= 0)
            .GroupBy(e => e.StudentId)
            .Select(g => new { StudentId = g.Key, Last = g.Max(e => e.Date) })
            .ToListAsync(ct);

        return rows
            .Where(x => !string.IsNullOrEmpty(x.Last))
            .ToDictionary(x => x.StudentId, x => x.Last!, StringComparer.Ordinal);
    }

    /// <summary>
    /// «FAOL O'QUVCHI» sanog'i — <see cref="KpiActiveStudentRule.IsActive"/> orqali.
    /// </summary>
    /// <remarks>
    /// ⚠️ A'zolik holati <c>Status</c> ustunidan EMAS, SANALARDAN tiklanadi
    /// (<see cref="CourseAnalytics.WasActiveAt"/>): <c>Status</c> JORIY holatni bildiradi —
    /// bugun muzlatilgan a'zolik oy boshida faol bo'lgan bo'lishi mumkin
    /// (<c>.claude/rules/course-analytics.md</c> §2). Yopilgan davrlar (<c>PastPeriods</c>) ham
    /// shu funksiya ichida tekshiriladi, ya'ni muzlatib qayta aktivlashtirilgan a'zolik tarixi
    /// yo'qolmaydi.
    /// </remarks>
    private static int ActiveCount(StudentBase b, string asOfIso)
    {
        var asOf = ParseDate(asOfIso) ?? AppClock.Today;
        var lastPresent = string.CompareOrdinal(asOfIso, b.Win.FirstIso) <= 0
            ? b.LastPresentAtStart : b.LastPresentAtEnd;

        var count = 0;
        foreach (var studentId in b.StudentIds)
        {
            var rows = b.Members.GetValueOrDefault(studentId);
            var hasActive = rows is not null && rows.Any(r => WasActive(r, asOfIso));
            if (!hasActive) continue;

            if (KpiActiveStudentRule.IsActive(
                    hasActiveMembership: true,
                    lastPresentDate: lastPresent.GetValueOrDefault(studentId),
                    oldestUnpaidDueDate: OldestUnpaidDue(b, studentId, asOfIso[..7]),
                    isArchived: b.Archived.Contains(studentId),
                    asOf: asOf))
                count++;
        }
        return count;
    }

    /// <summary>A'zolik shu sanada FAOL edimi — <see cref="CourseAnalytics.WasActiveAt"/> nusxasi emas,
    /// AYNAN o'sha funksiya (qoida bitta joyda qolsin).</summary>
    private static bool WasActive(MemberRow r, string dateIso) =>
        CourseAnalytics.WasActiveAt(
            new CourseAnalytics.MembershipRow(
                r.StudentId, r.GroupId, JoinedAt: "", r.ActivatedAt, r.LeftAt, r.FrozenAt,
                r.Status, r.IsActive, MonthlyFee: 0m, r.PastPeriods),
            dateIso);

    /// <summary>
    /// Eng ESKI to'lanmagan hisobning muddati ("yyyy-MM-dd"), qarz yo'q bo'lsa <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <b>"TO'LANMAGAN" ning ta'rifi</b> (<c>StudentLedger</c> bilan bir xil, chunki o'quvchi
    /// kartochkasida ko'rinadigan balans AYNAN shu mantiqdan chiqadi):
    /// <list type="bullet">
    ///   <item>oyning to'lash kerak bo'lgan summasi = <c>Amount − Discount</c> (100% chegirma —
    ///     "to'langan", qarz emas);</item>
    ///   <item>to'lovlar O'QUVCHI darajasida, ENG ESKI oydan boshlab (FIFO) taqsimlanadi;</item>
    ///   <item>vozvrat MANFIY to'lov (qarzni qaytaradi).</item>
    /// </list>
    /// ⚠️ Guruh kesimi HISOBGA OLINMAYDI. Sabab: to'lovlarning ko'pchiligi guruhga
    /// teglanmagan (bitta guruhli o'quvchida avtomatik teglanadi, ko'p guruhlida esa
    /// hovuzdan taqsimlanadi). Har hisobni faqat AYNAN o'sha (o'quvchi, guruh, oy) ga
    /// teglangan to'lov bilan solishtirsak, deyarli hamma o'quvchi "qarzdor" bo'lib chiqar va
    /// FAOL O'QUVCHILAR SONI nolga tushib ketardi — ya'ni butun Bonus A yo'qolardi.
    ///
    /// ⚠️ <paramref name="asOfMonth"/> dan KEYINGI oylarning hisobi qaralmaydi: kelasi oyga
    /// yozib qo'yilgan hisob "bugungi qarz" emas.
    /// </remarks>
    private static string? OldestUnpaidDue(StudentBase b, string studentId, string asOfMonth)
    {
        if (!b.Charges.TryGetValue(studentId, out var charges)) return null;

        var pool = 0m;
        if (b.Pays.TryGetValue(studentId, out var pays))
            foreach (var p in pays)
            {
                // Qaysi oyga tegishli ekani ko'rsatilgan bo'lsa — o'sha oy, aks holda to'langan
                // SANA (eski, oysiz yozuvlar) bo'yicha.
                var key = string.IsNullOrEmpty(p.Month)
                    ? (p.Date.Length >= 7 ? p.Date[..7] : "")
                    : p.Month;
                if (key.Length >= 7 && string.CompareOrdinal(key[..7], asOfMonth) > 0) continue;
                pool += p.Amount;
            }

        foreach (var c in charges)
        {
            if (c.Month.Length < 7 || string.CompareOrdinal(c.Month[..7], asOfMonth) > 0) break;
            var effective = c.Amount - c.Discount;
            if (effective <= 0) continue;
            if (pool >= effective) { pool -= effective; continue; }
            // Shu oy to'liq qoplanmadi — muddat: hisob yozilgan sana, bo'lmasa oyning 1-kuni.
            return c.Date.Length >= 10 ? c.Date[..10] : c.Month + "-01";
        }
        return null;
    }

    /// <summary>Markaz bo'yicha UMUMIY qarz — <paramref name="throughMonth"/> oxirigacha.</summary>
    private static decimal DebtTotal(StudentBase b, string throughMonth)
    {
        var total = 0m;
        foreach (var studentId in b.StudentIds)
        {
            if (!b.Charges.TryGetValue(studentId, out var charges)) continue;
            var charged = charges
                .Where(c => c.Month.Length >= 7 && string.CompareOrdinal(c.Month[..7], throughMonth) <= 0)
                .Sum(c => Math.Max(0m, c.Amount - c.Discount));
            var paid = 0m;
            if (b.Pays.TryGetValue(studentId, out var pays))
                paid = pays
                    .Where(p =>
                    {
                        var key = string.IsNullOrEmpty(p.Month)
                            ? (p.Date.Length >= 7 ? p.Date[..7] : "")
                            : p.Month;
                        return key.Length >= 7 && string.CompareOrdinal(key[..7], throughMonth) <= 0;
                    })
                    .Sum(p => p.Amount);
            // ⚠️ AVANS (ortiqcha to'lov) markaz qarzini KAMAYTIRMAYDI: bir o'quvchining oldindan
            // to'lagani boshqasining qarzini yopmaydi. Shuning uchun har o'quvchi alohida
            // 0 dan pastga tushmaydi.
            var owed = charged - paid;
            if (owed > 0) total += owed;
        }
        return total;
    }

    /// <summary>Markazdagi FAOL o'quvchilar soni — birlik iqtisodiyoti uchun yengil yo'l.</summary>
    private static async Task<int> CenterActiveCountAsync(IAppDbContext db, string asOfIso, CancellationToken ct)
    {
        var win = Window(asOfIso.Length >= 7 ? asOfIso[..7] : asOfIso);
        var b = await LoadStudentBaseAsync(db, win, ct);
        return ActiveCount(b, asOfIso);
    }

    private static async Task<CenterFacts> LoadCenterAsync(
        IAppDbContext db, MonthWindow win,
        Dictionary<string, Dictionary<string, double>> snapshots, CancellationToken ct)
    {
        var b = await LoadStudentBaseAsync(db, win, ct);

        // OY BOSHI — snapshotdan. Snapshot yo'q oy uchun JONLI tiklanadi va bayroq qo'yiladi:
        // jimgina taxminiy raqam bilan ishlash "aniq hisob" taassurotini berardi.
        var snap = snapshots.GetValueOrDefault(SnapKey(KpiConst.Retention, ""))
                   ?? snapshots.GetValueOrDefault(SnapKey(KpiConst.Retention, string.Empty));
        var hasSnap = snap is not null
                      && (snap.ContainsKey(SnapActiveCount) || snap.ContainsKey(SnapDebtTotal));

        var activeStart = hasSnap && snap!.TryGetValue(SnapActiveCount, out var a)
            ? (int)Math.Round(a)
            : ActiveCount(b, win.FirstIso);
        var debtStart = hasSnap && snap!.TryGetValue(SnapDebtTotal, out var d)
            ? (decimal)d
            : DebtTotal(b, win.PrevMonth);

        var activeEnd = ActiveCount(b, win.AsOfEndIso);

        var (leftControlled, unknownLeft) = await LeaversAsync(db, b, win, ct);
        var courseFinished = await CourseFinishedAsync(db, b, win, ct);

        var extended = await db.StudentExtensions.AsNoTracking()
            .Where(x => string.Compare(x.Date, win.FirstIso) >= 0
                        && string.Compare(x.Date, win.LastIso) <= 0)
            .Select(x => x.StudentId)
            .Distinct()
            .CountAsync(ct);

        // YIG'ILGAN QARZ — pul SHU OYDA tushgan (`Date`), qaysi oy uchun ekani muhim emas:
        // savol "xodim shu oyda qancha pul undirdi". Vozvrat AYIRILADI.
        var collectedRows = await db.FinanceTransactions.AsNoTracking()
            .Where(t => ((t.Direction == "income" && t.Category == "tuition")
                         || (t.Direction == "expense" && t.Category == "refund"))
                        && string.Compare(t.Date, win.FirstIso) >= 0
                        && string.Compare(t.Date, win.LastIso) <= 0)
            .Select(t => new { t.Amount, t.Direction })
            .ToListAsync(ct);
        var debtCollected = collectedRows.Sum(t => t.Direction == "expense" ? -t.Amount : t.Amount);

        return new CenterFacts(activeStart, activeEnd, leftControlled, unknownLeft,
            courseFinished, extended, debtStart, Math.Max(0m, debtCollected), !hasSnap);
    }

    /// <summary>
    /// KETGANLAR: nazorat ostidagi sabab bilan ketganlar va SABABI YOZILMAGANLAR.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>GURUH ALMASHTIRISH KETISH EMAS.</b> Bitta a'zolik yopilib, o'quvchi boshqa guruhda
    /// qolgan bo'lsa (daraja tugadi, jadval mos kelmadi) u markazni TASHLAB KETMAGAN. Shuning
    /// uchun oy oxirida hamon faol a'zoligi bor o'quvchi sanoqdan chiqariladi — aks holda har
    /// guruh almashtirish "ketdi" bo'lib ko'rinib, ketish foizi qo'rquvli, ammo YOLG'ON chiqardi
    /// (aynan shu xato <c>.claude/rules/course-analytics.md</c> §1 da tasvirlangan).
    ///
    /// ⚠️ <b>SABAB AUDIT MATNIDAN o'qiladi.</b> Ketish sababi <c>StudentGroup</c> da SAQLANMAYDI —
    /// u <c>audit.Record("Membership", ...)</c> ning izoh matniga «… — sabab: X» ko'rinishida
    /// tushadi (<c>ClassesController.RemoveMember</c>). Boshqa manba yo'q, shuning uchun matn
    /// shu yerda parse qilinadi va <c>ActionReason.OutOfControl</c> bilan solishtiriladi.
    /// Sabab topilmasa — "noma'lum" (jarima), <b>nazoratdan tashqari EMAS</b>: sababsiz ketish
    /// modulning butun ma'nosini (nima tuzatish kerakligini) yo'q qiladi.
    /// </remarks>
    private static async Task<(int Controlled, int Unknown)> LeaversAsync(
        IAppDbContext db, StudentBase b, MonthWindow win, CancellationToken ct)
    {
        var left = await db.StudentGroups.AsNoTracking()
            .Where(m => m.LeftAt != null
                        && string.Compare(m.LeftAt, win.FirstIso) >= 0
                        && string.Compare(m.LeftAt, win.LastIso) <= 0)
            .Select(m => new { m.StudentId, m.GroupId })
            .ToListAsync(ct);

        // Oy oxirida hamon FAOL a'zoligi borlar — guruh almashtirgan, ketmagan.
        var stillHere = left
            .Select(x => x.StudentId)
            .Distinct()
            .Where(sid => b.Members.GetValueOrDefault(sid)?.Any(r => WasActive(r, win.AsOfEndIso)) == true)
            .ToHashSet(StringComparer.Ordinal);

        var leavers = left.Where(x => !stillHere.Contains(x.StudentId)).ToList();
        if (leavers.Count == 0) return (0, 0);

        // Nazoratdan TASHQARI sabablarning yorliqlari (ko'chish, sog'liq, oila).
        var outOfControl = await db.ActionReasons.AsNoTracking()
            .Where(r => r.OutOfControl)
            .Select(r => r.Label)
            .ToListAsync(ct);

        var nextMonthIso = win.First.AddMonths(1).ToString("yyyy-MM-dd");
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType == "Membership"
                        && string.Compare(l.Timestamp, win.FirstIso) >= 0
                        && string.Compare(l.Timestamp, nextMonthIso) < 0)
            .Select(l => new { l.EntityId, l.Summary })
            .ToListAsync(ct);

        // EntityId = "{groupId}:{studentId}" — `Membership` naqshi.
        var reasonByPair = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in logs)
        {
            var reason = ReasonFromSummary(l.Summary);
            if (reason is null) continue;
            reasonByPair[l.EntityId] = reason;
        }

        int controlled = 0, unknown = 0;
        foreach (var studentId in leavers.Select(x => x.StudentId).Distinct())
        {
            // O'quvchi bir vaqtda bir NECHTA guruhdan chiqarilgan bo'lishi mumkin — sabab
            // birortasidan topilsa yetadi (odam bitta marta ketadi).
            var reason = leavers
                .Where(x => x.StudentId == studentId)
                .Select(x => reasonByPair.GetValueOrDefault($"{x.GroupId}:{studentId}"))
                .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));

            if (string.IsNullOrWhiteSpace(reason)) { unknown++; controlled++; continue; }
            if (outOfControl.Any(o => string.Equals(o, reason, StringComparison.OrdinalIgnoreCase))) continue;
            controlled++;
        }
        return (controlled, unknown);
    }

    /// <summary>Audit izohidan «… — sabab: X» qismini ajratadi (boshqa manba yo'q).</summary>
    private static string? ReasonFromSummary(string? summary)
    {
        if (string.IsNullOrEmpty(summary)) return null;
        const string marker = "sabab: ";
        var i = summary.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var value = summary[(i + marker.Length)..].Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// KURSI SHU OYDA TUGAGANLAR — uzaytirish foizining MAXRAJI.
    /// </summary>
    /// <remarks>⚠️ Faqat AKTIVLASHTIRILGAN a'zolik sanaladi (<c>ActivatedAt</c> bo'sh emas):
    /// sinovda kelib qolgan odam "kursni tugatgan" emas, ya'ni undan uzaytirish kutilmaydi va
    /// maxrajga qo'shilsa uzaytirish foizi soxta pasayardi.</remarks>
    private static async Task<int> CourseFinishedAsync(
        IAppDbContext db, StudentBase b, MonthWindow win, CancellationToken ct)
    {
        var groupIds = await db.Classes.AsNoTracking()
            .Where(g => g.Status == "completed"
                        || (g.EndDate != null
                            && string.Compare(g.EndDate, win.FirstIso) >= 0
                            && string.Compare(g.EndDate, win.LastIso) <= 0))
            .Select(g => g.Id)
            .ToListAsync(ct);
        if (groupIds.Count == 0) return 0;

        var set = groupIds.ToHashSet(StringComparer.Ordinal);
        return b.Members.Values
            .SelectMany(rows => rows)
            .Where(r => set.Contains(r.GroupId) && r.ActivatedAt.Length >= 10)
            .Select(r => r.StudentId)
            .Distinct()
            .Count();
    }

    /* =========================================================================================
     *  HISOBNI YIG'ISH
     * ====================================================================================== */

    private static KpiMonthDto Build(MonthCtx ctx, string userId)
    {
        var profile = ctx.Profiles.FirstOrDefault(p => p.UserId == userId);
        var role = profile?.RoleCode ?? KpiConst.Intake;
        var name = ctx.UserName(userId);
        var month = ctx.Win.Month;

        // TASDIQLANGAN OY — MUZLATILGAN qiymatlar (jonli hisob EMAS).
        if (ctx.Results.TryGetValue(userId, out var stored) && stored.Status == KpiConst.MonthConfirmed)
            return Frozen(stored, name);

        var ruleSet = KpiVersioning.RuleFor(ctx.Rules, role, month);
        if (ruleSet is null)
            return Empty(userId, name, role, month,
                "Bu rol uchun qoidalar to'plami topilmadi. «Qoidalar» sahifasidan seed qiling.");

        var rules = DeserializeRules(ruleSet.Json) ?? KpiRuleSeed.For(role);
        var salaryRow = KpiVersioning.SalaryFor(ctx.Salaries, userId, month);
        var baseSalary = salaryRow?.BaseSalary ?? 0m;
        var guarantee = GuaranteeActive(profile, month);

        var effort = ctx.Effort.GetValueOrDefault(userId)
                     ?? new EffortFacts(0, 0, 0, 0, 0);
        var (efficiency, effSource, effWarning) = Efficiency(effort);

        var inputs = new List<KpiInputDto>();
        var conversions = new List<KpiCoefDto>();
        var coefs = new List<KpiCoefDto>();
        var lines = new List<KpiMoneyLineDto>();
        var warnings = new List<string>();
        if (effWarning is not null) warnings.Add(effWarning);
        if (salaryRow is null)
            warnings.Add("Bu oy uchun oklad versiyasi yo'q — oklad 0 deb olindi.");

        inputs.Add(new KpiInputDto("baseSalary", "Oklad (shu oy)", (double)baseSalary,
            salaryRow is null
                ? "Oklad versiyasi topilmadi (KpiProfileSalary)"
                : $"Oklad versiyasi: {salaryRow.EffectiveFrom} dan",
            "/admin/boshqaruv/kpi/qoidalar", Auto: true));

        inputs.Add(new KpiInputDto("efficiency", "Samaradorlik", Math.Round(efficiency, 4),
            effSource, LinkToday, Auto: true));
        inputs.Add(new KpiInputDto("checklistDone", "Cheklist: bajarilgan bandlar", effort.ChecklistDone,
            $"Kunlik cheklist belgilari, oy ichida ({effort.ChecklistDone}/{effort.ChecklistTotal}); "
            + "«tegishli emas» maxrajga kirmaydi", LinkToday, Auto: true));
        inputs.Add(new KpiInputDto("tasksOnTime", "Topshiriq: muddatida bajarilgan", effort.TasksOnTime,
            $"Topshiriqlar: muddati oy ichida va bugungacha kelgan ({effort.TasksOnTime}/{effort.TasksDue})",
            LinkTasks, Auto: true));
        inputs.Add(new KpiInputDto("tickets", "Tasdiqlangan tiketlar", effort.Tickets,
            "Sifat nazorati: holati «tasdiqlangan» tiketlar, sanasi oy ichida", LinkTickets, Auto: true));

        decimal bonusTotal, fineTotal, computed, salary;
        bool guaranteeApplied, capExceeded;
        KpiPlanDto? plan = null;

        if (role == KpiConst.Retention)
        {
            var c = ctx.Center ?? new CenterFacts(0, 0, 0, 0, 0, 0, 0m, 0m, true);
            var i = new KpiRetentionInputs(
                c.ActiveStart, c.LeftControlled, c.ActiveEnd, c.CourseFinished, c.Extended,
                c.DebtStart, c.DebtCollected, efficiency, c.UnknownReasonLeft, effort.Tickets);
            var r = KpiCalculator.Retention(i, rules, baseSalary, guarantee);

            inputs.Insert(1, new KpiInputDto("activeStart", "Oy boshidagi faol o'quvchilar", c.ActiveStart,
                c.SnapshotMissing
                    ? "⚠️ Oy boshi snapshoti YO'Q — sanalardan jonli tiklandi (taxminiy)"
                    : "Oy boshi snapshoti (muzlatilgan raqam)",
                LinkStudents, Auto: true, Estimated: c.SnapshotMissing));
            inputs.Insert(2, new KpiInputDto("activeEnd", "Oy oxiridagi faol o'quvchilar", c.ActiveEnd,
                "Faol a'zolik + oxirgi 30 kunda darsga kelgan + 45 kundan eski qarzi yo'q + arxivda emas",
                LinkStudents, Auto: true));
            inputs.Insert(3, new KpiInputDto("leftControlled", "Ketganlar (nazorat ostidagi sabab)", c.LeftControlled,
                "Guruhdan chiqarilgan va oy oxirida boshqa guruhda ham qolmagan o'quvchilar; "
                + "sababi «nazoratdan tashqari» belgilanganlari CHIQARILADI",
                LinkHistory, Auto: true));
            inputs.Insert(4, new KpiInputDto("unknownReasonLeft", "Ketish sababi yozilmaganlar", c.UnknownReasonLeft,
                "Chiqarish yozuvida sabab ko'rsatilmagan holatlar (O'zgarishlar tarixi)",
                LinkHistory, Auto: true));
            inputs.Insert(5, new KpiInputDto("courseFinished", "Kursi tugaganlar", c.CourseFinished,
                "Guruhi «tugatilgan» yoki tugash sanasi oy ichida bo'lgan guruhlarning aktivlashtirilgan a'zolari",
                LinkClasses, Auto: true));
            inputs.Insert(6, new KpiInputDto("extended", "Uzaytirganlar", c.Extended,
                "Uzaytirish hodisalari (StudentExtension), sanasi oy ichida", LinkClasses, Auto: true));
            inputs.Insert(7, new KpiInputDto("debtStart", "Oy boshidagi qarz", (double)c.DebtStart,
                c.SnapshotMissing
                    ? "⚠️ Oy boshi snapshoti YO'Q — o'tgan oy oxirigacha hisob/to'lov farqidan tiklandi"
                    : "Oy boshi snapshoti (muzlatilgan raqam)",
                LinkFinance, Auto: true, Estimated: c.SnapshotMissing));
            inputs.Insert(8, new KpiInputDto("debtCollected", "Oy ichida yig'ilgan to'lov", (double)c.DebtCollected,
                "Moliya: «tuition» tushumi, sanasi oy ichida (vozvrat ayirilgan)", LinkFinance, Auto: true));

            conversions.Add(new KpiCoefDto("churnRate", "Ketish foizi", r.ChurnRate, r.RetentionCoef,
                r.RetentionTier?.Note ?? "Pog'ona topilmadi."));
            conversions.Add(new KpiCoefDto("extRate", "Uzaytirish foizi", r.ExtRate, r.ExtensionCoef,
                r.ExtensionTier?.Note ?? "Pog'ona topilmadi."));
            conversions.Add(new KpiCoefDto("debtRateFact", "Qarz yig'ish foizi", r.DebtRateFact, 1,
                $"Koeffitsient bermaydi — yig'ilgan qarzdan olinadigan ulushni belgilaydi "
                + $"({FormatPercent(r.DebtPercent)}; norma {FormatPercent(rules.DebtRateNorm)} dan past bo'lsa yarmi)."));

            coefs.Add(new KpiCoefDto("retention", "Ushlab qolish koeffitsienti", r.ChurnRate, r.RetentionCoef,
                r.RetentionTier?.Note ?? "Pog'ona topilmadi."));
            coefs.Add(new KpiCoefDto("extension", "Uzaytirish koeffitsienti", r.ExtRate, r.ExtensionCoef,
                r.ExtensionTier?.Note ?? "Pog'ona topilmadi."));
            coefs.Add(new KpiCoefDto("efficiency", "Samaradorlik koeffitsienti", efficiency, r.EffCoef,
                r.EffTier?.Note ?? "Pog'ona topilmadi."));

            lines.Add(new KpiMoneyLineDto("base", "Oklad", r.BaseSalary));
            lines.Add(new KpiMoneyLineDto("bonusA", "Bonus A — ushlab qolish", r.BonusA,
                $"{c.ActiveEnd} × {Money(rules.ActiveStudentBonus)} × {r.RetentionCoef:0.##}"));
            lines.Add(new KpiMoneyLineDto("bonusB", "Bonus B — uzaytirish", r.BonusB,
                $"{c.Extended} × {Money(rules.ExtensionBonus)} × {r.ExtensionCoef:0.##}"));
            lines.Add(new KpiMoneyLineDto("bonusC", "Bonus C — qarz yig'ish", r.BonusC,
                $"{Money(c.DebtCollected)} × {FormatPercent(r.DebtPercent)}"));
            lines.Add(new KpiMoneyLineDto("bonusTotal", "Bonus jami (samaradorlik bilan)", r.BonusTotal,
                $"(A + B + C) × {r.EffCoef:0.##}"));
            lines.Add(new KpiMoneyLineDto("fineUnknown", "Jarima — sababsiz ketish", -r.FineUnknown,
                $"{c.UnknownReasonLeft} × {Money(rules.UnknownReasonFine)}"));
            lines.Add(new KpiMoneyLineDto("fineTickets", "Jarima — tiketlar", -r.FineTickets,
                $"{effort.Tickets} × {Money(rules.TicketFine)}"));
            lines.Add(new KpiMoneyLineDto("total", "JAMI", r.Salary,
                r.GuaranteeApplied ? "Kafolat qo'llandi." : null));

            bonusTotal = r.BonusTotal;
            fineTotal = r.FineUnknown + r.FineTickets;
            computed = r.Computed;
            salary = r.Salary;
            guaranteeApplied = r.GuaranteeApplied;
            capExceeded = r.CapExceeded;

            if (c.SnapshotMissing)
                warnings.Add("Oy boshi snapshoti yo'q — «boshidagi faol» va «boshidagi qarz» taxminiy.");
        }
        else
        {
            var f = ctx.Intake.GetValueOrDefault(userId) ?? new IntakeFacts(0, 0, 0, 0);
            var i = new KpiIntakeInputs(f.Leads, f.TrialCame, f.Contracts, efficiency, effort.Tickets);
            var r = KpiCalculator.Intake(i, rules, baseSalary, guarantee);

            inputs.Insert(1, new KpiInputDto("leads", "Lidlar", f.Leads,
                "Lidlar: yaratilgan sana oy ichida; mas'ul — shu xodim yoki biriktirilmagan",
                LinkLeads, Auto: true));
            inputs.Insert(2, new KpiInputDto("leadsUnassigned", "Shundan biriktirilmagan", f.Unassigned,
                "Mas'uli ko'rsatilmagan lidlar — HAR KIRUVCHI adminga sanaladi. "
                + "Lidlarni biriktirsangiz raqam aniq bo'ladi.", LinkLeads, Auto: true));
            inputs.Insert(3, new KpiInputDto("trialCame", "Sinov darsiga KELGANLAR", f.TrialCame,
                "Sinov darslari: kelgan sana oy ichida (yoki natijasi «keldi/qoldi/ketdi» va rejasi oy ichida)",
                LinkLeads, Auto: true));
            inputs.Insert(4, new KpiInputDto("contracts", "Shartnomalar", f.Contracts,
                "Lid o'quvchiga aylantirilgan: yopgan xodim — shu xodim, yopilgan sana oy ichida",
                LinkLeads, Auto: true));

            conversions.Add(new KpiCoefDto("convLeadToTrial", "Lid → sinovga kelish", r.ConvLeadToTrial,
                r.TierCoef, r.ConvTier?.Note ?? "Pog'ona topilmadi."));
            conversions.Add(new KpiCoefDto("convTrialToDeal", "Kelgan → shartnoma", r.ConvTrialToDeal,
                1, r.QualityPenalty == 0
                    ? $"Norma bajarildi ({FormatPercent(rules.QualityNorm)} va undan yuqori) — jarima yo'q."
                    : $"Normadan past — konversiya koeffitsientiga {r.QualityPenalty:0.##} qo'shiladi."));
            conversions.Add(new KpiCoefDto("convLeadToDeal", "Lid → shartnoma", r.ConvLeadToDeal, 1,
                "Umumiy voronka ko'rsatkichi — koeffitsient bermaydi, bonusga ta'sir qilmaydi."));

            coefs.Add(new KpiCoefDto("conversion", "Konversiya koeffitsienti", r.ConvLeadToTrial, r.ConvCoef,
                Join(r.ConvTier?.Note,
                    r.QualityPenalty == 0 ? null : $"Sifat jarimasi {r.QualityPenalty:0.##}.",
                    r.TicketPenalty == 0 ? null : $"Tiket jarimasi {r.TicketPenalty:0.##}.")));
            coefs.Add(new KpiCoefDto("efficiency", "Samaradorlik koeffitsienti", efficiency, r.EffCoef,
                r.EffTier?.Note ?? "Pog'ona topilmadi."));

            lines.Add(new KpiMoneyLineDto("base", "Oklad", r.BaseSalary));
            lines.Add(new KpiMoneyLineDto("bonus", "Bonus — shartnomalar", r.Bonus,
                $"{f.Contracts} × {Money(rules.ContractBase)} × {r.ConvCoef:0.##} × {r.EffCoef:0.##}"));
            lines.Add(new KpiMoneyLineDto("fineTickets", "Jarima — tiketlar", -r.TicketFine,
                $"{effort.Tickets} × {Money(rules.TicketFine)}"));
            lines.Add(new KpiMoneyLineDto("total", "JAMI", r.Salary,
                r.GuaranteeApplied ? "Kafolat qo'llandi." : null));

            plan = new KpiPlanDto(
                r.PlanContracts, r.PlanDone, r.LeadFloorApplied, rules.LeadFloor,
                DailyLeads(rules), DailyTouches(rules), DailyTrials(rules), DailyContracts(rules));

            bonusTotal = r.Bonus;
            fineTotal = r.TicketFine;
            computed = r.Computed;
            salary = r.Salary;
            guaranteeApplied = r.GuaranteeApplied;
            capExceeded = r.CapExceeded;
        }

        var draft = ctx.Results.GetValueOrDefault(userId);
        return new KpiMonthDto(
            userId, name, role, KpiRuleSeed.RoleLabel(role), month,
            inputs, conversions, coefs, lines,
            baseSalary, bonusTotal, fineTotal, computed, salary,
            guaranteeApplied, capExceeded, rules.MonthlyCap,
            draft?.Status ?? KpiConst.MonthDraft, draft?.ConfirmedBy, draft?.ConfirmedAt,
            ruleSet.Id, RulesMissing: false,
            SnapshotMissing: role == KpiConst.Retention && (ctx.Center?.SnapshotMissing ?? true),
            plan,
            warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    /// <summary>Tasdiqlangan (muzlatilgan) oyni saqlangan qiymatlardan tiklaydi.</summary>
    /// <remarks>⚠️ Kirish raqamlari va koeffitsientlar JSON'dan o'qiladi — qayta HISOBLANMAYDI.
    /// Aks holda "tasdiqlangan" degan so'z ma'nosini yo'qotardi.</remarks>
    private static KpiMonthDto Frozen(KpiMonthResult r, string name)
    {
        var inputs = DeserializeList<KpiInputDto>(r.InputsJson);
        var stored = DeserializeCoefs(r.CoefsJson);
        return new KpiMonthDto(
            r.UserId, string.IsNullOrEmpty(r.UserName) ? name : r.UserName,
            r.RoleCode, KpiRuleSeed.RoleLabel(r.RoleCode), r.Month,
            inputs, stored.Conversions, stored.Coefs, stored.Lines,
            r.BaseSalary, r.BonusTotal, r.FineTotal,
            r.BaseSalary + r.BonusTotal - r.FineTotal, r.Salary,
            r.GuaranteeApplied, r.CapExceeded, stored.Cap,
            r.Status, r.ConfirmedBy, r.ConfirmedAt,
            r.RuleSetId, RulesMissing: false, SnapshotMissing: false,
            stored.Plan, r.Note);
    }

    /// <summary>Qoidalar topilmagan xodim uchun BO'SH (nol) hisob + sabab.</summary>
    /// <remarks>⚠️ Xatoga yiqilmaydi: bitta rolning qoidasi seed qilinmagani butun «Yopish»
    /// sahifasini ochilmaydigan qilib qo'yardi.</remarks>
    private static KpiMonthDto Empty(string userId, string name, string role, string month, string warning) =>
        new(userId, name, role, KpiRuleSeed.RoleLabel(role), month,
            [], [], [], [], 0m, 0m, 0m, 0m, 0m,
            false, false, null, KpiConst.MonthDraft, null, null,
            null, RulesMissing: true, SnapshotMissing: false, null, warning);

    /// <summary>Kafolat shu oyda amal qiladimi.</summary>
    private static bool GuaranteeActive(KpiProfile? p, string month)
    {
        if (p is null || string.IsNullOrWhiteSpace(p.GuaranteeUntilMonth)) return false;
        var until = p.GuaranteeUntilMonth!.Length >= 7 ? p.GuaranteeUntilMonth![..7] : p.GuaranteeUntilMonth!;
        return string.CompareOrdinal(month, until) <= 0;
    }

    /// <summary>Samaradorlik: cheklist → topshiriqlar → 1.0 (ogohlantirish bilan).</summary>
    private static (double Value, string Source, string? Warning) Efficiency(EffortFacts e)
    {
        if (e.ChecklistTotal > 0)
            return ((double)e.ChecklistDone / e.ChecklistTotal,
                $"Kunlik cheklist: {e.ChecklistDone}/{e.ChecklistTotal} band bajarilgan "
                + "(«tegishli emas» maxrajga kirmaydi)", null);

        if (e.TasksDue > 0)
            return ((double)e.TasksOnTime / e.TasksDue,
                $"Cheklist belgilari yo'q — topshiriqlardan olindi: {e.TasksOnTime}/{e.TasksDue} muddatida",
                "Bu oyda cheklist belgilanmagan — samaradorlik topshiriqlar bo'yicha hisoblandi.");

        return (1.0,
            "O'lchanmadi: bu oyda cheklist belgisi ham, muddati kelgan topshiriq ham yo'q",
            "Samaradorlik O'LCHANMADI (cheklist ham, topshiriq ham yo'q) — koeffitsient 1.0 deb olindi.");
    }

    /* =========================================================================================
     *  «BUGUN» — kunlik normalar, cheklist, signallar, ogohlantirish ro'yxatlari
     * ====================================================================================== */

    // Kunlik normalar (spetsifikatsiya §16) — qoidalar to'plamidan HISOBLANADI, hardcode EMAS:
    // rahbar oylik rejani yoki ish kunlarini o'zgartirsa kunlik norma ham o'zi o'zgarsin.
    private static double DailyContracts(KpiRuleSetJson r) =>
        r.WorkDays <= 0 ? 0 : Math.Round((double)r.MonthlyContractPlan / r.WorkDays, 2);

    private static double DailyTrials(KpiRuleSetJson r) =>
        r.WorkDays <= 0 || r.PlanTrialToContract <= 0
            ? 0 : Math.Round(r.MonthlyContractPlan / r.PlanTrialToContract / r.WorkDays, 2);

    private static double DailyLeads(KpiRuleSetJson r) =>
        r.WorkDays <= 0 || r.PlanTrialToContract <= 0 || r.PlanLeadToTrial <= 0
            ? 0 : Math.Round(r.MonthlyContractPlan / r.PlanTrialToContract / r.PlanLeadToTrial / r.WorkDays, 2);

    private static double DailyTouches(KpiRuleSetJson r) => Math.Round(DailyLeads(r) * r.TouchesPerLead, 2);

    /// <summary>Kunlik muloqot vaqti normasi (spetsifikatsiya §16: 2 soat).</summary>
    private const double DailyTalkHoursNorm = 2.0;

    private static async Task<List<KpiNormDto>> IntakeNormsAsync(
        IAppDbContext db, string userId, string dayIso, KpiRuleSetJson rules, CancellationToken ct)
    {
        var nextIso = (ParseDate(dayIso) ?? AppClock.Today).AddDays(1).ToString("yyyy-MM-dd");

        var leads = await db.Leads.AsNoTracking()
            .CountAsync(l => string.Compare(l.CreatedAt, dayIso) >= 0
                             && string.Compare(l.CreatedAt, nextIso) < 0
                             && (l.AssigneeUserId == userId || l.AssigneeUserId == null), ct);

        var trials = await db.TrialLessons.AsNoTracking()
            .CountAsync(t => t.AttendedAt == dayIso, ct);

        var contracts = await db.Leads.AsNoTracking()
            .CountAsync(l => l.ClosedAt == dayIso && l.ClosedByUserId == userId, ct);

        // Qo'ng'iroqlar — `Call` da vaqtlar `DateTime` (boshqa entitylardan FARQLI).
        var from = (ParseDate(dayIso) ?? AppClock.Today).ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(1);
        var calls = await db.Calls.AsNoTracking()
            .Where(c => c.OperatorUserId == userId && c.StartedAt >= from && c.StartedAt < to)
            .Select(c => c.DurationSeconds)
            .ToListAsync(ct);

        return
        [
            new KpiNormDto("leads", "Yangi lid", leads, DailyLeads(rules), "ta", false, LinkLeads,
                "Bugun yaratilgan lidlar (mas'ul — siz yoki biriktirilmagan)"),
            new KpiNormDto("touches", "Teginish (qo'ng'iroq/xabar)", calls.Count, DailyTouches(rules), "ta",
                false, LinkCalls, $"Bitta lidga {rules.TouchesPerLead:0.##} teginish rejasi"),
            new KpiNormDto("trialCame", "Sinovga kelgan", trials, DailyTrials(rules), "ta", false, LinkLeads,
                "Bugun sinov darsiga KELGANLAR (markaz bo'yicha)"),
            new KpiNormDto("contracts", "Shartnoma", contracts, DailyContracts(rules), "ta", false, LinkLeads,
                "Siz yopgan shartnomalar"),
            new KpiNormDto("talkTime", "Muloqot vaqti", Math.Round(calls.Sum() / 3600.0, 2),
                DailyTalkHoursNorm, "soat", false, LinkCalls, "Bugungi qo'ng'iroqlar davomiyligi"),
        ];
    }

    private static async Task<List<KpiNormDto>> RetentionNormsAsync(
        IAppDbContext db, string dayIso, CancellationToken ct)
    {
        var absentToday = await db.JournalEntries.AsNoTracking()
            .CountAsync(e => e.Date == dayIso && !e.Present, ct);

        var leftToday = await db.StudentGroups.AsNoTracking()
            .CountAsync(m => m.LeftAt == dayIso, ct);

        var extendedToday = await db.StudentExtensions.AsNoTracking()
            .CountAsync(x => x.Date == dayIso, ct);

        var collected = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.Direction == "income" && t.Category == "tuition" && t.Date == dayIso)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var streaks = await AbsenceStreaksAsync(db, dayIso, ct);

        return
        [
            new KpiNormDto("absentToday", "Bugun kelmaganlar", absentToday, 0, "ta", true, LinkAttendance,
                "Har biriga xabar yuborilishi kerak (norma: yopilmagan holat qolmasin)"),
            new KpiNormDto("streak3", "Ketma-ket 3 dars kelmaganlar", streaks.Three.Count, 0, "ta", true,
                LinkAttendance, "Norma 0 — har biriga QO'NG'IROQ qilinadi (xabar emas)"),
            new KpiNormDto("leftToday", "Bugun ketganlar", leftToday, 0, "ta", true, LinkStudents,
                "Har biriga qo'ng'iroq qilinib, ketish sababi aniqlanishi shart"),
            new KpiNormDto("extendedToday", "Uzaytirganlar", extendedToday, 1.5, "ta", false, LinkClasses,
                "Kunlik reja 1–2 uzaytirish"),
            new KpiNormDto("collectedToday", "Bugun yig'ilgan to'lov", (double)collected, 0, "so'm", false,
                LinkFinance, "Moliya: bugungi «tuition» tushumi"),
        ];
    }

    /// <summary>Ketma-ket kelmagan o'quvchilar (2 va 3 dars).</summary>
    private sealed record Streaks(List<KpiAlertRowDto> Two, List<KpiAlertRowDto> Three);

    /// <remarks>
    /// ⚠️ Ketma-ketlik SAQLANMAYDI — u har so'rovda jurnaldan hisoblanadi. Saqlansa, davomat
    /// keyinroq tuzatilganda (o'qituvchi ertasi kuni to'g'rilaydi) ro'yxat eskirib qolar va
    /// admin allaqachon kelgan o'quvchiga qo'ng'iroq qilardi.
    ///
    /// ⚠️ Oyna <see cref="AlertLookbackDays"/> kun: butun jurnalni o'qishning hojati yo'q,
    /// "oxirgi 2–3 dars" doim shu oraliqda.
    /// </remarks>
    private static async Task<Streaks> AbsenceStreaksAsync(IAppDbContext db, string dayIso, CancellationToken ct)
    {
        var day = ParseDate(dayIso) ?? AppClock.Today;
        var fromIso = day.AddDays(-AlertLookbackDays).ToString("yyyy-MM-dd");

        var rows = await db.JournalEntries.AsNoTracking()
            .Where(e => string.Compare(e.Date, fromIso) >= 0 && string.Compare(e.Date, dayIso) <= 0)
            .Select(e => new { e.StudentId, e.Date, e.Present })
            .ToListAsync(ct);

        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var names = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && !s.IsArchived)
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        var two = new List<KpiAlertRowDto>();
        var three = new List<KpiAlertRowDto>();
        foreach (var g in rows.GroupBy(r => r.StudentId))
        {
            if (!names.TryGetValue(g.Key, out var name)) continue;   // arxivlangan — ro'yxatga kirmaydi
            var ordered = g.OrderByDescending(r => r.Date, StringComparer.Ordinal).ToList();
            var streak = 0;
            foreach (var e in ordered)
            {
                if (e.Present) break;
                streak++;
            }
            if (streak >= 3)
                three.Add(new KpiAlertRowDto(g.Key, name,
                    $"Ketma-ket {streak} dars kelmadi (oxirgisi {ordered[0].Date})",
                    $"{LinkStudents}/{g.Key}"));
            else if (streak == 2)
                two.Add(new KpiAlertRowDto(g.Key, name,
                    $"Ketma-ket 2 dars kelmadi (oxirgisi {ordered[0].Date})",
                    $"{LinkStudents}/{g.Key}"));
        }
        return new Streaks(two, three);
    }

    private static async Task<List<KpiAlertListDto>> RetentionAlertListsAsync(
        IAppDbContext db, string dayIso, CancellationToken ct)
    {
        var streaks = await AbsenceStreaksAsync(db, dayIso, ct);
        var day = ParseDate(dayIso) ?? AppClock.Today;

        // BUGUN TO'LOV MUDDATI KELGANLAR: hisob qatori aynan shu sana bilan yozilgan
        // (to'liq oy uchun `Date = "{oy}-01"`, qisman hisoblarda aktivlashtirish/muzlatish sanasi).
        var dueCharges = await db.MonthlyCharges.AsNoTracking()
            .Where(c => c.Date == dayIso)
            .Select(c => new { c.StudentId, c.Month, c.Amount, c.Discount })
            .ToListAsync(ct);
        var dueIds = dueCharges.Select(c => c.StudentId).Distinct().ToList();
        var dueNames = await db.Students.AsNoTracking()
            .Where(s => dueIds.Contains(s.Id) && !s.IsArchived)
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var dueRows = dueCharges
            .Where(c => dueNames.ContainsKey(c.StudentId))
            .GroupBy(c => c.StudentId)
            .Select(g => new KpiAlertRowDto(g.Key, dueNames[g.Key],
                $"{g.First().Month} uchun {Money(g.Sum(x => x.Amount - x.Discount))} so'm",
                $"{LinkStudents}/{g.Key}"))
            .ToList();

        // KURSI 3 HAFTA ICHIDA TUGAYDIGAN GURUHLAR — uzaytirish suhbatining boshlanish nuqtasi.
        var endFrom = dayIso;
        var endTo = day.AddDays(EndingSoonDays).ToString("yyyy-MM-dd");
        var ending = await db.Classes.AsNoTracking()
            .Where(g => !g.IsArchived && g.EndDate != null
                        && string.Compare(g.EndDate, endFrom) >= 0
                        && string.Compare(g.EndDate, endTo) <= 0)
            .Select(g => new { g.Id, g.Name, g.EndDate })
            .ToListAsync(ct);
        var endingRows = ending
            .OrderBy(g => g.EndDate, StringComparer.Ordinal)
            .Select(g => new KpiAlertRowDto(g.Id, g.Name, $"Tugash sanasi: {g.EndDate}",
                $"{LinkClasses}/{g.Id}"))
            .ToList();

        return
        [
            new KpiAlertListDto("streak3", "Ketma-ket 3 dars kelmaganlar — QO'NG'IROQ", LinkAttendance, streaks.Three),
            new KpiAlertListDto("streak2", "Ketma-ket 2 dars kelmaganlar — ro'yxat", LinkAttendance, streaks.Two),
            new KpiAlertListDto("dueToday", "Bugun to'lov muddati kelganlar", LinkFinance, dueRows),
            new KpiAlertListDto("endingSoon", $"{EndingSoonDays} kun ichida tugaydigan guruhlar", LinkClasses, endingRows),
        ];
    }

    private static async Task<List<KpiChecklistBlockDto>> ChecklistBlocksAsync(
        IAppDbContext db, string userId, string role, string dayIso, CancellationToken ct)
    {
        var template = await db.ChecklistTemplates.AsNoTracking()
            .Where(t => t.RoleCode == role && t.IsActive)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (template is null) return [];

        var items = await db.ChecklistTemplateItems.AsNoTracking()
            .Where(i => i.TemplateId == template.Id)
            .OrderBy(i => i.Order).ThenBy(i => i.No)
            .ToListAsync(ct);
        if (items.Count == 0) return [];

        var itemIds = items.Select(i => i.Id).ToList();
        var entries = await db.ChecklistEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.Date == dayIso && itemIds.Contains(e.ItemId))
            .ToDictionaryAsync(e => e.ItemId, StringComparer.Ordinal, ct);

        // ⚠️ Bloklar shablon TARTIBIDA qoladi (alifbo bo'yicha saralanmaydi): "08:00" va "13:00"
        // matn sifatida to'g'ri saralansa ham, sarlavhalar erkin matn ("KUN BOSHI") va
        // saralash kun tartibini buzib yuborardi.
        var blocks = new List<KpiChecklistBlockDto>();
        foreach (var item in items)
        {
            var entry = entries.GetValueOrDefault(item.Id);
            var dto = new KpiChecklistItemDto(
                item.Id, item.No, item.Text, item.Norm, item.KpiTag, item.CriterionNo,
                item.AutoCheckKey,
                // Belgilanmagan band — "tegishli emas" EMAS, balki hali belgilanmagan.
                // Klient uchun ikkalasi ham "bosilmagan" ko'rinishda, lekin samaradorlik
                // maxrajiga faqat done/failed kiradi (yozuv yo'q — maxrajda ham yo'q).
                entry?.State ?? "",
                entry?.Source ?? KpiConst.SourceManual,
                entry?.Note);

            var block = blocks.FirstOrDefault(b => b.TimeBlock == item.TimeBlock);
            if (block is null) blocks.Add(new KpiChecklistBlockDto(item.TimeBlock, [dto]));
            else block.Items.Add(dto);
        }
        return blocks;
    }

    private static async Task<List<KpiSignalDto>> SignalsAsync(
        IAppDbContext db, string userId, string role, string dayIso, string month, CancellationToken ct)
    {
        var overdue = await db.WorkTasks.AsNoTracking()
            .CountAsync(t => t.AssigneeId == userId && !t.IsArchived && t.CompletedAt == null
                             && t.DueDate != null && string.Compare(t.DueDate, dayIso) < 0, ct);

        var win = Window(month);
        var tickets = await db.KpiTickets.AsNoTracking()
            .CountAsync(t => t.UserId == userId && t.Status == KpiConst.TicketConfirmed
                             && string.Compare(t.Date, win.FirstIso) >= 0
                             && string.Compare(t.Date, win.LastIso) <= 0, ct);

        var proposed = await db.KpiTickets.AsNoTracking()
            .CountAsync(t => t.UserId == userId && t.Status == KpiConst.TicketProposed, ct);

        var signals = new List<KpiSignalDto>
        {
            new("overdueTasks", "Kechikkan topshiriq", overdue, overdue == 0 ? "ok" : "bad", LinkTasks),
            new("ticketsMonth", "Shu oydagi tiket", tickets, tickets == 0 ? "ok" : tickets < 5 ? "warn" : "bad",
                LinkTickets),
            new("ticketsProposed", "Ko'rib chiqilmagan tiket", proposed, proposed == 0 ? "ok" : "warn", LinkTickets),
        };

        if (role != KpiConst.Retention)
        {
            // VAZIFASIZ LID — kiruvchi adminning eng ko'p uchraydigan bo'shlig'i: lid ochilgan,
            // lekin keyingi qadam belgilanmagan (30 kundan eski, hali yopilmagan).
            var staleFrom = (ParseDate(dayIso) ?? AppClock.Today).AddDays(-30).ToString("yyyy-MM-dd");
            var stale = await db.Leads.AsNoTracking()
                .CountAsync(l => l.AssigneeUserId == userId && l.ConvertedStudentId == null
                                 && string.Compare(l.CreatedAt, staleFrom) < 0, ct);
            signals.Add(new KpiSignalDto("staleLeads", "30 kundan eski ochiq lid", stale,
                stale == 0 ? "ok" : "warn", LinkLeads));
        }

        return signals;
    }

    /* =========================================================================================
     *  YORDAMCHILAR
     * ====================================================================================== */

    /// <summary>Shu oyda amal qiladigan qoidalar JSON'i (topilmasa <c>null</c>).</summary>
    private static async Task<KpiRuleSetJson?> RulesForAsync(
        IAppDbContext db, string role, string month, CancellationToken ct)
    {
        var all = await db.KpiRuleSets.AsNoTracking().Where(r => r.RoleCode == role).ToListAsync(ct);
        var found = KpiVersioning.RuleFor(all, role, month);
        return found is null ? null : DeserializeRules(found.Json);
    }

    /// <summary>Qoidalar JSON'ini o'qiydi; buzuq bo'lsa <c>null</c> (hisob xatoga yiqilmasin).</summary>
    public static KpiRuleSetJson? DeserializeRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<KpiRuleSetJson>(json, JsonOpts); }
        catch (JsonException) { return null; }
    }

    /// <summary>Qoidalarni saqlash uchun JSON.</summary>
    public static string SerializeRules(KpiRuleSetJson rules) => JsonSerializer.Serialize(rules, JsonOpts);

    /// <summary>Tasdiqlangan oy uchun saqlanadigan koeffitsient/qator to'plami.</summary>
    /// <remarks>⚠️ Faqat SON emas, IZOHLAR ham saqlanadi: bir yildan keyin "nega shuncha chiqqan"
    /// degan savolga javob bera olmasak, muzlatishning ma'nosi qolmasdi.</remarks>
    public sealed record FrozenCoefs(
        List<KpiCoefDto> Conversions, List<KpiCoefDto> Coefs, List<KpiMoneyLineDto> Lines,
        decimal? Cap, KpiPlanDto? Plan);

    /// <summary>Muzlatish uchun kirish raqamlarini JSON qiladi.</summary>
    public static string SerializeInputs(List<KpiInputDto> inputs) => JsonSerializer.Serialize(inputs, JsonOpts);

    /// <summary>Muzlatish uchun koeffitsient/qatorlarni JSON qiladi.</summary>
    public static string SerializeCoefs(KpiMonthDto m) =>
        JsonSerializer.Serialize(new FrozenCoefs(m.Conversions, m.Coefs, m.Lines, m.Cap, m.Plan), JsonOpts);

    private static FrozenCoefs DeserializeCoefs(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var v = JsonSerializer.Deserialize<FrozenCoefs>(json, JsonOpts);
                if (v is not null) return v with { Conversions = v.Conversions ?? [], Coefs = v.Coefs ?? [], Lines = v.Lines ?? [] };
            }
            catch (JsonException) { /* buzuq yozuv — bo'sh ro'yxat, summa baribir qatorda saqlangan */ }
        }
        return new FrozenCoefs([], [], [], null, null);
    }

    private static List<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? []; }
        catch (JsonException) { return []; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static DateOnly? ParseDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        var s = iso.Length > 10 ? iso[..10] : iso;
        return DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
    }

    private static string Money(decimal v) =>
        v.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatPercent(double v) =>
        (v * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string Join(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
