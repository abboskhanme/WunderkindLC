using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QUVCHINING JURNALDAGI O'Z QATORI — YAGONA mantiq.
///
/// <para>Ilgari bu hisob faqat <c>StudentAttendanceController.Journal</c> ichida edi (admin
/// profilidagi jurnal modali). O'quvchi ilovasiga «Umumiy statistika» (hafta/oy oralig'i) kerak
/// bo'lganda mantiqni NUSXALASH — eng xavfli yo'l edi: <c>RecordedAt</c>, <c>memberStart</c>,
/// <c>memberEnd</c> va "noma'lum dars" qoidalari ikki joyda ayri ketib, admin bir foizni,
/// o'quvchi ilovasi boshqasini ko'rsatardi. Shuning uchun yadro shu yerga ajratildi va
/// controller ham, portal ham AYNAN shu funksiyalarni chaqiradi.</para>
///
/// <para>Qoidalar manbai — <c>.claude/rules/journal.md</c>:</para>
/// <list type="bullet">
///   <item><b>blocked</b> — dars guruh <see cref="Group.StartDate"/>idan oldin, a'zolik
///     boshlanishidan (<see cref="Bounds.MemberStart"/>) oldin yoki a'zolik tugashidan
///     (<see cref="Bounds.MemberEnd"/>) keyin bo'lsa: bu dars o'quvchiga umuman tegishli emas.</item>
///   <item><b>presentDefaultFrom</b> (<see cref="StudentGroup.RecordedAt"/>) — standart
///     "davomat olindi + yozuv yo'q = keldi" qoidasi FAQAT a'zolik tizimga HAQIQATDA kiritilgan
///     sanadan keyin ishlaydi (JoinedAt/ActivatedAt orqaga sanalishi mumkin).</item>
///   <item><b>unknown</b> — o'sha sanadan OLDINGI, yozuvsiz, lekin o'tilgan darslar: na "keldi",
///     na "kelmadi". Jamlanmaga (held/attended/absent/late) KIRMAYDI — aks holda orqaga sanab
///     qo'shilgan o'quvchining davomat foizi asossiz tushib ketardi.</item>
/// </list>
/// </summary>
public static class StudentJournalBuilder
{
    /// <summary>
    /// Sana oralig'ining eng katta uzunligi (kun). Himoya: o'quvchi ilovasi "2 yillik statistika"
    /// so'rasa, oraliq oyma-oy aylanib chiqiladi va har oy uchun kataklar quriladi — bu bitta
    /// so'rovda o'nlab ming katak degani. QIRQIB olish emas, 400 xato tanlandi: jimgina qirqilgan
    /// javob mijozda "ma'lumot yo'qolgan" bo'lib ko'rinardi, xato esa aniq aytadi.
    /// </summary>
    public const int MaxRangeDays = 400;

    /// <summary>
    /// A'zolikdan kelib chiqadigan uchta SANA CHEGARASI. Bo'sh (null) = cheklov yo'q
    /// (eski, to'ldirilmagan a'zoliklar uchun ataylab shunday — cheklovni "bor" deb talqin qilsak
    /// eski jurnal butunlay bo'shab qolardi).
    /// </summary>
    public sealed record Bounds(string? MemberStart, string? MemberEnd, string? PresentDefaultFrom);

    /// <summary>
    /// Bitta darsdagi o'quvchi holati (ichki, boyitilgan ko'rinish). <see cref="StudentJournalCellDto"/>
    /// dan farqi: <see cref="Unknown"/> bayrog'i, dars raqami va mavzu/uyga vazifa matni ham bor —
    /// o'quvchi ilovasidagi "har darsga" ro'yxati shulardan quriladi.
    /// </summary>
    public sealed record Cell(
        string Date, int Period, bool Conducted, bool Blocked, bool Present, bool Unknown,
        int? Grade, string? ReasonName, string? ReasonShort, bool IsLate,
        int Homework, int Behavior, MasteryLevel? Mastery,
        string Topic, string HomeworkText);

    /// <summary>Bitta KUNDAGI dars yozuvlari jamlanmasi (bir kunda bir necha <see cref="LessonNote"/> bo'lishi mumkin).</summary>
    public sealed record LessonInfo(bool Conducted, bool AttendanceTaken, int Period, string Topic, string Homework);

    /// <summary>
    /// A'zolikdan chegaralarni oladi. <b>Muzlatilganda</b> chegara <see cref="StudentGroup.FrozenAt"/>
    /// (o'sha kunning o'zi hali hisoblanadi), aks holda <see cref="StudentGroup.LeftAt"/> — muzlatilgan
    /// a'zolikda LeftAt to'ldirilgan bo'lishi mumkin, lekin u chiqib ketish emas.
    /// </summary>
    public static Bounds BoundsOf(StudentGroup? m)
    {
        var start = JournalService.MemberStart(m);
        string? end = null;
        if (m is not null)
        {
            end = string.Equals(m.Status, "frozen", StringComparison.Ordinal)
                ? (m.FrozenAt is { Length: >= 10 } ? m.FrozenAt[..10] : null)
                : (m.LeftAt is { Length: >= 10 } ? m.LeftAt[..10] : null);
        }
        var recorded = m?.RecordedAt is { Length: >= 10 } ? m.RecordedAt[..10] : null;
        return new Bounds(start, end, recorded);
    }

    /// <summary>
    /// Dars yozuvlarini SANA bo'yicha jamlaydi. Bir kunda bir nechta yozuv bo'lsa: "o'tildi" va
    /// "davomat olindi" — birortasida bo'lsa yetarli (yig'ma YOKI), mavzu/uyga vazifa esa birinchi
    /// (eng kichik <see cref="LessonNote.Period"/>) darsdan olinadi.
    /// <b>AttendanceTaken faqat Conducted yozuvdan hisobga olinadi</b> — o'tilmagan darsda "davomat
    /// olindi" bayrog'i ma'nosiz, va u standart "keldi" ni yoqib yuborardi.
    /// </summary>
    public static Dictionary<string, LessonInfo> AggregateNotes(IEnumerable<LessonNote> notes)
    {
        var map = new Dictionary<string, LessonInfo>(StringComparer.Ordinal);
        foreach (var n in notes.OrderBy(x => x.Period))
        {
            if (map.TryGetValue(n.Date, out var prev))
            {
                map[n.Date] = prev with
                {
                    Conducted = prev.Conducted || n.Conducted,
                    AttendanceTaken = prev.AttendanceTaken || (n.Conducted && n.AttendanceTaken),
                };
            }
            else
            {
                map[n.Date] = new LessonInfo(
                    n.Conducted, n.Conducted && n.AttendanceTaken,
                    n.Period <= 0 ? 1 : n.Period, n.Topic ?? "", n.Homework ?? "");
            }
        }
        return map;
    }

    /// <summary>
    /// Kataklarni quradi — <b>DB so'rovi yo'q</b> (sof funksiya, testlash uchun qulay).
    /// Kiruvchi ma'lumot chaqiruvchi tomonidan bir marta (batch) yuklanadi.
    /// </summary>
    public static List<Cell> BuildCells(
        Group group, Bounds bounds, IEnumerable<string> dates,
        IReadOnlyDictionary<string, JournalEntry> entryByDate,
        IReadOnlyDictionary<string, LessonInfo> lessonByDate,
        IReadOnlyDictionary<string, AbsenceReason> reasons)
    {
        var groupStart = group.StartDate is { Length: >= 10 } ? group.StartDate[..10] : null;
        var cells = new List<Cell>();
        foreach (var date in dates)
        {
            var blocked = (groupStart is not null && string.CompareOrdinal(date, groupStart) < 0)
                || (bounds.MemberStart is not null && string.CompareOrdinal(date, bounds.MemberStart) < 0)
                || (bounds.MemberEnd is not null && string.CompareOrdinal(date, bounds.MemberEnd) > 0);

            entryByDate.TryGetValue(date, out var e);
            AbsenceReason? reason = e?.ReasonId is not null ? reasons.GetValueOrDefault(e.ReasonId) : null;
            lessonByDate.TryGetValue(date, out var note);
            var isConducted = note?.Conducted == true;

            // ANIQ Present belgisi (katakdagi "Keldi" / "hammasi keldi") davomat olinmagan kunda ham
            // "keldi" hisoblanadi. STANDART "keldi" (davomat olingan, lekin bu o'quvchida yozuv yo'q)
            // esa faqat a'zolik tizimga kiritilgan sanadan (PresentDefaultFrom) keyin.
            var defaultPresent = note?.AttendanceTaken == true
                && (bounds.PresentDefaultFrom is null
                    || string.CompareOrdinal(date, bounds.PresentDefaultFrom) >= 0);
            var present = !blocked && e?.Grade is null && reason is null
                && (e?.Present == true || defaultPresent);

            var unknown = !blocked && isConducted && e is null
                && bounds.PresentDefaultFrom is not null
                && string.CompareOrdinal(date, bounds.PresentDefaultFrom) < 0;

            cells.Add(new Cell(
                date, note?.Period ?? 1, isConducted, blocked, present, unknown,
                e?.Grade, reason?.Name, reason?.Short, reason?.IsLate ?? false,
                e?.Homework ?? 0, e?.Behavior ?? 0, e?.Mastery,
                note?.Topic ?? "", note?.Homework ?? ""));
        }
        return cells;
    }

    /* =========================================================================================
     *  BATCH yuklash — bir nechta guruh × bir nechta oy, N+1 so'rovsiz
     * ========================================================================================= */

    /// <summary>
    /// Berilgan guruhlar × oylar uchun kataklar (guruh id → kataklar, sana bo'yicha tartiblangan).
    /// Barcha DB so'rovlari BIR MARTA, guruhlar ro'yxati bo'yicha bajariladi — guruh soniga qarab
    /// so'rov ko'paymaydi.
    /// </summary>
    private static async Task<Dictionary<string, List<Cell>>> CellsAsync(
        IAppDbContext db, string studentId,
        IReadOnlyList<Group> groups,
        IReadOnlyDictionary<string, StudentGroup> membershipByGroup,
        IReadOnlyList<string> months,
        IReadOnlyDictionary<string, AbsenceReason> reasons)
    {
        var result = new Dictionary<string, List<Cell>>(StringComparer.Ordinal);
        if (groups.Count == 0 || months.Count == 0) return result;

        var gids = groups.Select(g => g.Id).ToList();
        // Sana oralig'i ("yyyy-MM-dd"): oyning birinchi kunidan "…-99" gacha. Sanalar leksikografik
        // tartibda saqlangani uchun "-99" o'sha oyning istalgan kunidan katta, keyingi oydan kichik.
        var minDate = months[0] + "-01";
        var maxDate = months[^1] + "-99";

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.StudentId == studentId && gids.Contains(e.ClassId)
                        && e.Date.CompareTo(minDate) >= 0 && e.Date.CompareTo(maxDate) <= 0)
            .ToListAsync();
        var notes = await db.LessonNotes.AsNoTracking()
            .Where(n => gids.Contains(n.ClassId) && n.Conducted
                        && n.Date.CompareTo(minDate) >= 0 && n.Date.CompareTo(maxDate) <= 0)
            .ToListAsync();
        var moves = await db.LessonReschedules.AsNoTracking()
            .Where(r => gids.Contains(r.ClassId))
            .Select(r => new { r.ClassId, r.FromDate, r.ToDate })
            .ToListAsync();

        var entriesByGroup = entries.ToLookup(e => e.ClassId, StringComparer.Ordinal);
        var notesByGroup = notes.ToLookup(n => n.ClassId, StringComparer.Ordinal);
        var movesByGroup = moves.ToLookup(m => m.ClassId, StringComparer.Ordinal);

        foreach (var g in groups)
        {
            var subjectId = g.CourseId ?? "";
            // Fan (kurs) bo'sh guruhda jurnal ham yo'q — admin mantig'i bilan bir xil.
            // Yozuv/dars fan bo'yicha ham filtrlanadi: guruhda eski, boshqa kursga tegishli
            // qatorlar qolgan bo'lishi mumkin.
            var groupEntries = string.IsNullOrEmpty(subjectId)
                ? new List<JournalEntry>()
                : entriesByGroup[g.Id].Where(e => e.SubjectId == subjectId).ToList();
            var groupNotes = string.IsNullOrEmpty(subjectId)
                ? new List<LessonNote>()
                : notesByGroup[g.Id].Where(n => n.SubjectId == subjectId).ToList();

            var entryByDate = groupEntries
                .GroupBy(e => e.Date, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var lessonByDate = AggregateNotes(groupNotes);

            var groupMoves = movesByGroup[g.Id]
                .Select(m => new JournalService.LessonMove(m.FromDate, m.ToDate)).ToList();
            var bounds = BoundsOf(membershipByGroup.GetValueOrDefault(g.Id));

            var cells = new List<Cell>();
            foreach (var month in months)
            {
                var dates = JournalService.EffectiveLessonDatesInMonth(g.Days, month, groupMoves);
                cells.AddRange(BuildCells(g, bounds, dates, entryByDate, lessonByDate, reasons));
            }
            result[g.Id] = cells;
        }
        return result;
    }

    /// <summary>O'quvchining a'zoliklari + guruhlari + nom lug'atlari (ikkala endpoint uchun bir xil).</summary>
    private static async Task<(List<StudentGroup> Memberships, List<Group> Groups,
        Dictionary<string, string> CourseNames, Dictionary<string, string> TeacherNames)>
        GroupsOfAsync(IAppDbContext db, string studentId)
    {
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.StudentId == studentId).ToListAsync();
        var mGroupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        var groups = mGroupIds.Count == 0
            ? new List<Group>()
            : await db.Classes.AsNoTracking().Where(c => mGroupIds.Contains(c.Id)).ToListAsync();

        var courseIds = groups.Select(g => g.CourseId ?? "").Where(x => x.Length > 0).Distinct().ToList();
        var teacherIds = groups.Select(g => g.TeacherId ?? "").Where(x => x.Length > 0).Distinct().ToList();
        var courseNames = courseIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Subjects.AsNoTracking().Where(s => courseIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name);
        var teacherNames = teacherIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.FullName);
        return (memberships, groups, courseNames, teacherNames);
    }

    /// <summary>
    /// Guruh tanlovi ro'yxati: avval TIRIK a'zoliklar, ular ichida HOLAT bo'yicha
    /// (faol → sinov → muzlatilgan), keyin nom bo'yicha.
    ///
    /// <para>⚠️ Birinchi band STANDART tanlanadi. Ilgari saralash faqat <c>IsActive</c> ga
    /// qarardi — muzlatish esa <c>IsActive</c> ni O'ZGARTIRMAYDI, ya'ni muzlatilgan va aktiv
    /// a'zolik teng chiqib, tartib ALIFBOGA tushardi: o'quvchi eski guruhida muzlatilib
    /// yangisida o'qiyotgan bo'lsa, jurnal ESKI guruh bilan ochilardi.</para>
    /// </summary>
    private static List<StudentJournalGroupDto> OptionsOf(
        List<Group> groups, List<StudentGroup> memberships,
        Dictionary<string, string> courseNames, Dictionary<string, string> teacherNames) =>
        groups
            .OrderByDescending(g => memberships.Any(m => m.GroupId == g.Id && m.IsActive))
            .ThenBy(g => memberships.Where(m => m.GroupId == g.Id && m.IsActive)
                .Select(m => MembershipLifecycle.StateRank(m.Status))
                .DefaultIfEmpty(int.MaxValue).Min())
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StudentJournalGroupDto(
                g.Id, g.Name,
                string.IsNullOrEmpty(g.CourseId) ? "" : courseNames.GetValueOrDefault(g.CourseId, ""),
                string.IsNullOrEmpty(g.TeacherId) ? "" : teacherNames.GetValueOrDefault(g.TeacherId, "")))
            .ToList();

    /* =========================================================================================
     *  1) ADMIN: bitta guruh + bitta OY (o'quvchi profilidagi jurnal modali)
     * ========================================================================================= */

    /// <summary>
    /// O'quvchining BITTA guruhdagi oylik jurnali (faqat o'qish). Guruh berilmasa — birinchi faol
    /// guruhi, oy berilmasa — oxirgi (joriy) oy. O'quvchi topilmasa <c>null</c>.
    /// <para>Bu — <c>GET /api/admin/student-attendance/journal</c> ning butun mantig'i; controller
    /// faqat 404 ni qaytaradi. Javob shakli TARIXAN bir xil qoladi (ishlab turgan admin ekrani).</para>
    /// </summary>
    public static async Task<StudentJournalDto?> GroupMonthAsync(
        IAppDbContext db, string studentId, string? groupId, string? month)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId);
        if (student is null) return null;

        var (memberships, groups, courseNames, teacherNames) = await GroupsOfAsync(db, studentId);
        if (groups.Count == 0)
            return new StudentJournalDto(student.Id, student.FullName, new(), "", new(), "", new(), 0, 0, 0, 0, 0);

        var options = OptionsOf(groups, memberships, courseNames, teacherNames);
        var gid = !string.IsNullOrWhiteSpace(groupId) && groups.Any(g => g.Id == groupId)
            ? groupId! : options[0].GroupId;
        var group = groups.First(g => g.Id == gid);
        var membership = memberships.FirstOrDefault(m => m.GroupId == gid);

        // Mavjud oylar: guruh boshlanish sanasi (yoki a'zolik) oyidan joriy oygacha.
        var cur = TuitionService.CurrentMonth();
        var starts = new List<string>();
        if (group.StartDate is { Length: >= 7 }) starts.Add(group.StartDate[..7]);
        if (membership?.JoinedAt is { Length: >= 7 }) starts.Add(membership.JoinedAt[..7]);
        var from = starts.Count > 0 ? starts.Min()! : cur;
        if (string.CompareOrdinal(from, cur) > 0) from = cur;
        var months = TuitionService.MonthRange(from, cur).ToList();
        if (months.Count == 0) months.Add(cur);
        var resolved = !string.IsNullOrEmpty(month) && months.Contains(month) ? month! : months[^1];

        var reasons = await db.AbsenceReasons.AsNoTracking().ToDictionaryAsync(r => r.Id);
        var membershipByGroup = new Dictionary<string, StudentGroup>(StringComparer.Ordinal);
        if (membership is not null) membershipByGroup[gid] = membership;
        var byGroup = await CellsAsync(
            db, studentId, new[] { group }, membershipByGroup, new[] { resolved }, reasons);
        var cells = byGroup.GetValueOrDefault(gid) ?? new List<Cell>();

        var live = cells.Where(c => !c.Blocked && c.Conducted && !c.Unknown).ToList();
        var absent = live.Count(c => c.ReasonName is not null && !c.IsLate);
        var late = live.Count(c => c.ReasonName is not null && c.IsLate);
        // "Keldi" — faqat rassmiy tasdiqlangan (present) yoki baho olingan kunlar.
        var attended = live.Count(c => c.Present || c.Grade.HasValue);
        var grades = cells.Where(c => c.Grade.HasValue).Select(c => (double)c.Grade!.Value).ToList();

        var dtoCells = cells.Select(c => new StudentJournalCellDto(
            c.Date, c.Conducted, c.Blocked, c.Present,
            c.Grade, c.ReasonName, c.ReasonShort, c.IsLate,
            c.Homework, c.Behavior, c.Mastery)).ToList();

        return new StudentJournalDto(
            student.Id, student.FullName, options, gid, months, resolved, dtoCells,
            live.Count, attended, absent, late,
            grades.Count > 0 ? Math.Round(grades.Average(), 1) : 0);
    }

    /* =========================================================================================
     *  2) O'QUVCHI ILOVASI: sana ORALIG'I (hafta/oy) — «Umumiy statistika»
     * ========================================================================================= */

    /// <summary>
    /// So'rovdagi oraliqni tekshiradi va normallashtiradi ("yyyy-MM-dd"). Bo'sh berilsa — JORIY OY
    /// (oyning 1-kunidan oxirgi kunigacha). Bittasi berilsa — ikkinchisi o'sha oydan to'ldiriladi.
    /// Xato bo'lsa <c>Error</c> to'ladi (controller 400 qaytaradi).
    /// </summary>
    public static (string From, string To, string? Error) NormalizeRange(string? from, string? to)
    {
        DateOnly? f = DateOnly.TryParse(from, out var fv) ? fv : null;
        DateOnly? t = DateOnly.TryParse(to, out var tv) ? tv : null;
        if (!string.IsNullOrWhiteSpace(from) && f is null) return ("", "", "«from» sanasi noto'g'ri");
        if (!string.IsNullOrWhiteSpace(to) && t is null) return ("", "", "«to» sanasi noto'g'ri");

        if (f is null && t is null)
        {
            var cur = TuitionService.CurrentMonth();
            var y = int.Parse(cur[..4]);
            var m = int.Parse(cur[5..7]);
            f = new DateOnly(y, m, 1);
            t = new DateOnly(y, m, DateTime.DaysInMonth(y, m));
        }
        // Bittasi berilgan bo'lsa — ikkinchisi o'sha OYning chekkasidan olinadi (ilova ba'zan
        // faqat boshlanish sanasini yuboradi; bo'sh javob o'rniga ma'noli oy qaytgani yaxshi).
        f ??= new DateOnly(t!.Value.Year, t.Value.Month, 1);
        t ??= new DateOnly(f.Value.Year, f.Value.Month, DateTime.DaysInMonth(f.Value.Year, f.Value.Month));

        if (f > t) return ("", "", "«from» sanasi «to» dan katta bo'lishi mumkin emas");
        if (t.Value.DayNumber - f.Value.DayNumber + 1 > MaxRangeDays)
            return ("", "", $"Oraliq juda uzun — ko'pi bilan {MaxRangeDays} kun");

        return (f.Value.ToString("yyyy-MM-dd"), t.Value.ToString("yyyy-MM-dd"), null);
    }

    /// <summary>
    /// O'quvchining SANA ORALIG'IDAGI jurnali: har darsga baho/davomat/uy vazifasi/xulq + jamlanma
    /// va fanlar kesimi. <paramref name="groupId"/> bo'sh bo'lsa — BARCHA guruhlar birlashtiriladi.
    /// O'quvchi topilmasa <c>null</c>.
    ///
    /// <para><b>Javobga faqat o'tilgan (conducted) va bloklanmagan darslar kiradi</b> — rejadagi,
    /// hali o'tilmagan dars o'quvchiga ko'rsatilmaydi. Jamlanma esa bundan tashqari NOMA'LUM
    /// (<see cref="Cell.Unknown"/>) darslarni ham chiqarib tashlaydi: ular ro'yxatda ko'rinadi
    /// (dars bo'lgan, mavzusi bor), lekin davomat foizini buzmaydi.</para>
    /// </summary>
    public static async Task<StudentPeriodJournalDto?> PeriodAsync(
        IAppDbContext db, string studentId, string from, string to, string? groupId)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId);
        if (student is null) return null;

        var (memberships, groups, courseNames, teacherNames) = await GroupsOfAsync(db, studentId);
        var options = OptionsOf(groups, memberships, courseNames, teacherNames);

        // Tanlangan guruh: berilgan bo'lsa FAQAT o'sha (o'quvchi a'zo bo'lgan guruhlar ichidan).
        // A'zo bo'lmagan guruh so'ralsa ro'yxat BO'SH qaytadi — begona o'quvchining jurnali
        // hech qachon ochilmaydi (guruh tanlovi baribir qaytadi, ilova ro'yxatni ko'rsata olsin).
        var selected = string.IsNullOrWhiteSpace(groupId)
            ? groups
            : groups.Where(g => g.Id == groupId).ToList();

        var empty = new StudentPeriodJournalDto(
            from, to, groupId ?? "", options,
            new StudentPeriodSummaryDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            new List<StudentPeriodSubjectDto>(), new List<StudentPeriodLessonDto>());
        if (selected.Count == 0) return empty;

        var months = TuitionService.MonthRange(from[..7], to[..7]).ToList();
        if (months.Count == 0) return empty;

        var membershipByGroup = memberships
            .GroupBy(m => m.GroupId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var reasons = await db.AbsenceReasons.AsNoTracking().ToDictionaryAsync(r => r.Id);
        var byGroup = await CellsAsync(db, studentId, selected, membershipByGroup, months, reasons);

        var lessons = new List<StudentPeriodLessonDto>();
        // Jamlanma NOMA'LUM darslarsiz hisoblanadi — shuning uchun "live" alohida yig'iladi.
        var live = new List<(string SubjectId, string SubjectName, Cell Cell)>();
        foreach (var g in selected)
        {
            var subjectId = g.CourseId ?? "";
            var subjectName = subjectId.Length == 0 ? "" : courseNames.GetValueOrDefault(subjectId, "");
            foreach (var c in byGroup.GetValueOrDefault(g.Id) ?? new List<Cell>())
            {
                if (c.Blocked || !c.Conducted) continue;
                if (string.CompareOrdinal(c.Date, from) < 0 || string.CompareOrdinal(c.Date, to) > 0) continue;
                lessons.Add(new StudentPeriodLessonDto(
                    c.Date, c.Period, g.Id, g.Name, subjectId, subjectName,
                    c.Topic, c.HomeworkText, c.Conducted, c.Present,
                    c.Grade, c.ReasonName, c.ReasonShort, c.IsLate,
                    c.Homework, c.Behavior, c.Mastery));
                if (!c.Unknown) live.Add((subjectId, subjectName, c));
            }
        }

        lessons = lessons
            .OrderBy(l => l.Date, StringComparer.Ordinal)
            .ThenBy(l => l.Period)
            .ThenBy(l => l.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var summary = SummaryOf(live.Select(x => x.Cell).ToList());
        var subjects = live
            .GroupBy(x => x.SubjectId, StringComparer.Ordinal)
            .Select(grp =>
            {
                var cells = grp.Select(x => x.Cell).ToList();
                var g2 = SummaryOf(cells);
                return new StudentPeriodSubjectDto(
                    grp.Key, grp.First().SubjectName, g2.Held, g2.Attended, g2.GradesCount, g2.AvgGrade);
            })
            .OrderByDescending(s => s.Held)
            .ThenBy(s => s.SubjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StudentPeriodJournalDto(from, to, groupId ?? "", options, summary, subjects, lessons);
    }

    /// <summary>
    /// Jamlanma — FAQAT "tirik" (o'tilgan, bloklanmagan, noma'lum bo'lmagan) kataklardan.
    /// <c>homeworkDone/Missed</c>: 1 = qildi, 2 = qilmadi; 3 (chala) ATAYIN ikkalasiga ham
    /// qo'shilmaydi — "chala qildi" na bajarilgan, na bajarilmagan.
    /// </summary>
    private static StudentPeriodSummaryDto SummaryOf(IReadOnlyList<Cell> live)
    {
        var held = live.Count;
        var attended = live.Count(c => c.Present || c.Grade.HasValue);
        var absent = live.Count(c => c.ReasonName is not null && !c.IsLate);
        var late = live.Count(c => c.ReasonName is not null && c.IsLate);
        var grades = live.Where(c => c.Grade.HasValue).Select(c => (double)c.Grade!.Value).ToList();
        return new StudentPeriodSummaryDto(
            held, attended, absent, late,
            held > 0 ? (int)Math.Round(attended * 100.0 / held, MidpointRounding.AwayFromZero) : 0,
            grades.Count,
            grades.Count > 0 ? Math.Round(grades.Average(), 1) : 0,
            live.Count(c => c.Homework == 1), live.Count(c => c.Homework == 2),
            live.Count(c => c.Behavior == 1), live.Count(c => c.Behavior == 2));
    }
}
