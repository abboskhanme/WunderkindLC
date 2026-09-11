using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'quvchi "BALL"i — guruh sahifasidagi "Reyting" tabi bilan BIR XIL formula:
/// <c>Ball = Σ(jurnal baholari) + Σ(bajarilgan baholash mezonlari) + Σ(qo'lda tuzatish)</c>.
///
/// <para><b>BALL PER-GURUH HISOBLANADI</b> (<see cref="ComputeByGroupAsync"/>): kalit —
/// <c>(o'quvchi, guruh)</c>. Ilgari hisob faqat <c>StudentId</c> bo'yicha guruhlanardi, ya'ni
/// ball hech qachon guruhlarga ajratilmagan: bir nechta guruhda o'qiydigan o'quvchining barcha
/// ballari QO'SHILIB, u markazda ham, HAR BIR guruh reytingida ham nohaq yuqorida turardi.</para>
///
/// <para><b>Markazdagi o'rin — O'RTACHA bo'yicha</b> (<see cref="SchoolAsync"/>):
/// <c>AvgBall = Jami ÷ (ball tushgan guruhlar soni)</c>. Maxraj ATAYIN "faol a'zoliklar soni"
/// emas: surat va maxraj bitta to'plamdan olinishi shart, aks holda o'qituvchisi hali baho
/// qo'ymagan yangi guruh o'quvchining o'rtachasini tushirib yuborardi. Bitta guruhli o'quvchida
/// <c>AvgBall == Ball</c> — ya'ni ko'pchilik uchun hech narsa o'zgarmaydi.</para>
///
/// <para><b>QO'LDA TUZATISH</b> (<see cref="StudentBallAdjustment"/>) hisobning ichida:
/// guruh bali = <c>Math.Max(0, hisoblangan + Σ Delta)</c> — manfiyga tushmaydi. Shu sababdan
/// tuzatish BARCHA reytinglarda (markaz, guruh, o'qituvchi) avtomatik hisobga olinadi.</para>
///
/// Manbalar: <see cref="JournalEntry.Grade"/> (jurnal bahosi), <see cref="CriterionGrade"/>
/// (<c>Done=true</c> belgilangan baholash mezonlari) va <see cref="StudentBallAdjustment"/>.
/// </summary>
public static class StudentBallService
{
    /// <summary>
    /// Ball tarkibi. Odatda BITTA guruh uchun (<see cref="ComputeByGroupAsync"/>).
    /// <para><paramref name="GroupBallSum"/> — faqat guruhlar bo'yicha JAMLANGAN statda to'ldiriladi
    /// (<see cref="ComputeAsync"/>): u yerda jami = har guruhda ALOHIDA 0 ga qirqilgan ballar
    /// yig'indisi bo'lishi shart. Aks holda bitta guruhdagi manfiy tuzatish boshqa guruh balini
    /// "yeb" qo'yardi.</para>
    /// </summary>
    public sealed record BallStat(
        int JournalTotal, int GradeCount, int CriteriaDone, int Adjustment = 0, int? GroupBallSum = null)
    {
        /// <summary>Amaldagi ball — hech qachon manfiy emas.</summary>
        public int Ball => GroupBallSum ?? Math.Max(0, JournalTotal + CriteriaDone + Adjustment);
        /// <summary>Tuzatishsiz, xom hisob (tarix/tafsilot oynasi uchun).</summary>
        public int Computed => JournalTotal + CriteriaDone;
        /// <summary>O'rtacha baho (baho qo'yilgan darslar bo'yicha); baho yo'q bo'lsa 0.</summary>
        public double Average => GradeCount > 0 ? Math.Round((double)JournalTotal / GradeCount, 1) : 0;
    }

    /// <summary>
    /// O'quvchining barcha guruhlari bo'yicha JAMLANGAN ball.
    /// <para><paramref name="GroupCount"/> — BALL TUSHGAN guruhlar soni (<c>Ball &gt; 0</c>),
    /// <paramref name="AvgBall"/> — markaz reytingi saralanadigan o'rtacha (1 kasrgacha).
    /// <paramref name="GroupBalls"/> — guruh → shu guruhdagi ball (guruh reytingi shundan quriladi).</para>
    /// </summary>
    public sealed record StudentBallTotals(
        int JournalTotal, int GradeCount, int CriteriaDone, int Adjustment,
        int Ball, double Average, int GroupCount, double AvgBall,
        IReadOnlyDictionary<string, int> GroupBalls);

    /// <summary>
    /// <b>PER-GURUH ball</b> — kalit <c>(StudentId, GroupId)</c>. <paramref name="groupIds"/> berilsa
    /// faqat shu guruhlar hisoblanadi (o'qituvchi reytingi / guruh tahlili); null — barcha guruhlar.
    ///
    /// <para><paramref name="month"/> ("yyyy-MM") berilsa — FAQAT shu oydagi ball. Bo'sh/null =
    /// UMUMIY (barcha vaqt) — eski xatti-harakat, hech narsa o'zgarmaydi. Qo'lda tuzatish
    /// (<see cref="StudentBallAdjustment"/>) YOZILGAN oyiga tegishli deb hisoblanadi
    /// (<c>CreatedAt</c> boshlanishi "yyyy-MM"), ya'ni oy kesimida ham hisobga olinadi.</para>
    /// </summary>
    public static async Task<Dictionary<(string StudentId, string GroupId), BallStat>> ComputeByGroupAsync(
        IAppDbContext db, IReadOnlyCollection<string>? groupIds = null, string? month = null)
    {
        var result = new Dictionary<(string, string), BallStat>();
        if (groupIds is { Count: 0 }) return result;
        // Bo'sh satr = filtr yo'q (Umumiy) — controllerdan "?month=" bo'sh kelsa ham to'g'ri ishlasin.
        var m = string.IsNullOrEmpty(month) ? null : month;

        // Jurnal baholari — GURUH (ClassId) ham guruhlash kalitida.
        var jq = db.JournalEntries.AsNoTracking().Where(e => e.Grade != null);
        if (groupIds is not null) jq = jq.Where(e => groupIds.Contains(e.ClassId));
        if (m is not null) jq = jq.Where(e => e.Date.StartsWith(m));
        var journal = await jq
            .GroupBy(e => new { e.StudentId, e.ClassId })
            .Select(g => new
            {
                g.Key.StudentId,
                GroupId = g.Key.ClassId,
                Total = g.Sum(x => x.Grade ?? 0),
                Count = g.Count(),
            })
            .ToListAsync();

        var cq = db.CriterionGrades.AsNoTracking().Where(g => g.Done);
        if (groupIds is not null) cq = cq.Where(g => groupIds.Contains(g.GroupId));
        if (m is not null) cq = cq.Where(g => g.Date.StartsWith(m));
        var criteria = await cq
            .GroupBy(g => new { g.StudentId, g.GroupId })
            .Select(g => new { g.Key.StudentId, g.Key.GroupId, Done = g.Count() })
            .ToListAsync();

        var aq = db.StudentBallAdjustments.AsNoTracking();
        if (groupIds is not null) aq = aq.Where(a => groupIds.Contains(a.GroupId));
        if (m is not null) aq = aq.Where(a => a.CreatedAt.StartsWith(m));
        var adjustments = await aq
            .GroupBy(a => new { a.StudentId, a.GroupId })
            .Select(g => new { g.Key.StudentId, g.Key.GroupId, Delta = g.Sum(x => x.Delta) })
            .ToListAsync();

        foreach (var j in journal)
            result[(j.StudentId, j.GroupId)] = new BallStat(j.Total, j.Count, 0);
        foreach (var c in criteria)
            result[(c.StudentId, c.GroupId)] = result.TryGetValue((c.StudentId, c.GroupId), out var b)
                ? b with { CriteriaDone = c.Done }
                : new BallStat(0, 0, c.Done);
        foreach (var a in adjustments)
            result[(a.StudentId, a.GroupId)] = result.TryGetValue((a.StudentId, a.GroupId), out var b)
                ? b with { Adjustment = a.Delta }
                : new BallStat(0, 0, 0, a.Delta);
        return result;
    }

    /// <summary>
    /// Per-guruh statlarni O'QUVCHI bo'yicha jamlaydi (jami, o'rtacha, guruhlar kesimi).
    /// Sof funksiya — testlanadi va bazaga tegmaydi.
    /// </summary>
    public static Dictionary<string, StudentBallTotals> AggregateByStudent(
        IReadOnlyDictionary<(string StudentId, string GroupId), BallStat> byGroup)
    {
        var result = new Dictionary<string, StudentBallTotals>();
        foreach (var g in byGroup.GroupBy(kv => kv.Key.StudentId))
        {
            int journal = 0, grades = 0, criteria = 0, adjust = 0, ball = 0, withBall = 0;
            var groupBalls = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in g)
            {
                var s = kv.Value;
                journal += s.JournalTotal;
                grades += s.GradeCount;
                criteria += s.CriteriaDone;
                adjust += s.Adjustment;
                // Har guruhda ALOHIDA 0 ga qirqiladi, keyin qo'shiladi.
                ball += s.Ball;
                if (s.Ball > 0) withBall++;      // maxraj: faqat ball TUSHGAN guruhlar
                groupBalls[kv.Key.GroupId] = s.Ball;
            }
            var average = grades > 0 ? Math.Round((double)journal / grades, 1) : 0;
            var avgBall = withBall > 0 ? Math.Round((double)ball / withBall, 1) : 0;
            result[g.Key] = new StudentBallTotals(
                journal, grades, criteria, adjust, ball, average, withBall, avgBall, groupBalls);
        }
        return result;
    }

    /// <summary>
    /// O'quvchi → JAMLANGAN ball tarkibi (barcha guruhlari qo'shilgan holda).
    /// <paramref name="groupIds"/> berilsa faqat shu guruhlar (o'qituvchi reytingi / guruh tahlili).
    /// <para>⚠️ Imzo va MA'NO ataylab o'zgarmadi (bitta guruh berilganda AYNAN o'sha guruh bali
    /// qaytadi — <c>GroupSnapshotBuilder</c> shunga tayanadi). Per-guruh kesim kerak bo'lsa
    /// <see cref="ComputeByGroupAsync"/> ishlatiladi.</para>
    /// </summary>
    public static async Task<Dictionary<string, BallStat>> ComputeAsync(
        IAppDbContext db, IReadOnlyCollection<string>? groupIds = null)
    {
        var byGroup = await ComputeByGroupAsync(db, groupIds);
        return AggregateByStudent(byGroup).ToDictionary(
            kv => kv.Key,
            kv => new BallStat(
                kv.Value.JournalTotal, kv.Value.GradeCount, kv.Value.CriteriaDone,
                kv.Value.Adjustment, GroupBallSum: kv.Value.Ball));
    }

    /// <summary>
    /// Markaz bo'yicha barcha (arxivlanmagan) o'quvchilar bali — admin ro'yxati ustuni uchun.
    /// <para><b>Markaz reytingi <c>AvgBall</c> bo'yicha saralanadi</b> (yig'indi emas): ko'p guruhda
    /// o'qiydigan o'quvchi shunchaki ko'p baho olgani uchun tepaga chiqib qolmasin.</para>
    /// </summary>
    public static async Task<List<StudentBallDto>> SchoolAsync(IAppDbContext db)
    {
        var totals = AggregateByStudent(await ComputeByGroupAsync(db));
        var ids = await db.Students.AsNoTracking().Where(s => !s.IsArchived).Select(s => s.Id).ToListAsync();
        return ids.Select(id =>
        {
            var b = totals.GetValueOrDefault(id);
            return b is null
                ? new StudentBallDto(id, 0, 0, 0, 0)
                : new StudentBallDto(id, b.JournalTotal, b.CriteriaDone, b.Ball, b.Average, b.AvgBall, b.GroupCount);
        }).ToList();
    }

    /// <summary>
    /// O'qituvchi guruhlaridagi o'quvchilar reytingi — <b>qator = (o'quvchi, GURUH)</b>.
    ///
    /// <para>Ilgari bitta o'quvchi bitta qator bo'lib, o'qituvchining BARCHA guruhlaridagi bali
    /// qo'shilardi: ikki guruhda o'qiydigan o'quvchi ikki barobar ball bilan birinchi chiqardi va
    /// "qaysi guruhda qanaqa" degan savolga javob yo'q edi. Endi har guruh uchun alohida qator,
    /// <c>Rank</c> esa GURUH ICHIDA (1,2,3...) — aralash o'rin emas.</para>
    ///
    /// <para>Ro'yxatning O'ZI ball kamayishi bo'yicha (podium/eski ilova shunga tayanadi);
    /// <c>Groups</c> maydoni orqaga moslik uchun SHU qatorning guruh nomi bilan to'ldiriladi —
    /// o'qituvchi ilovasining eski versiyalari uni matn bo'yicha filtrlaydi.</para>
    ///
    /// Faqat FAOL a'zolar va arxivlanmagan o'quvchilar. Davomat — SHU guruhda o'tilgan darslar
    /// bo'yicha (kech kelish sababi qatnashmaydi).
    ///
    /// <para><paramref name="month"/> ("yyyy-MM") — OY kesimi: ball, davomat va o'tilgan darslar
    /// faqat shu oy bo'yicha hisoblanadi. Bo'sh/null = <b>Umumiy</b> (barcha vaqt) — ESKI, standart
    /// xatti-harakat: parametrsiz chaqiruvlar (masalan <c>TeacherSnapshotBuilder</c>) o'zgarmaydi.</para>
    ///
    /// <para>Javobdagi <c>Months</c> — tanlash mumkin bo'lgan oylar (eng erta ma'lumot oyidan JORIY
    /// oygacha, uzluksiz): kelajakdagi oy ro'yxatga umuman tushmaydi, ya'ni UI bo'sh oyga o'tolmaydi.
    /// <c>Month</c> — aynan qaysi oy qaytarilgani ("" = Umumiy).</para>
    /// </summary>
    public static async Task<TeacherRatingDto> TeacherAsync(
        IAppDbContext db, Teacher teacher, string? month = null)
    {
        var mon = string.IsNullOrEmpty(month) ? null : month;
        var groups = await db.Classes.AsNoTracking()
            .Where(c => c.TeacherId == teacher.Id && !c.IsArchived)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();
        if (groups.Count == 0)
            return new TeacherRatingDto(
                teacher.Id, teacher.FullName, 0, 0, 0, new List<TeacherRatingRowDto>(),
                0, mon ?? "", new List<string> { TuitionService.CurrentMonth() });

        var groupIds = groups.Select(g => g.Id).ToList();
        var groupName = groups.ToDictionary(g => g.Id, g => g.Name);
        var months = await RatingMonthsAsync(db, groupIds);

        // Faol a'zoliklar: o'quvchi → shu o'qituvchining qaysi guruhlarida o'qiydi.
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => groupIds.Contains(sg.GroupId) && sg.IsActive)
            .Select(sg => new { sg.StudentId, sg.GroupId })
            .Distinct()
            .ToListAsync();
        var studentIds = memberships.Select(x => x.StudentId).Distinct().ToList();
        if (studentIds.Count == 0)
            return new TeacherRatingDto(
                teacher.Id, teacher.FullName, groups.Count, 0, 0, new List<TeacherRatingRowDto>(),
                0, mon ?? "", months);

        var studentName = (await db.Students.AsNoTracking()
                .Where(s => studentIds.Contains(s.Id) && !s.IsArchived)
                .Select(s => new { s.Id, s.FullName })
                .ToListAsync())
            .ToDictionary(s => s.Id, s => s.FullName);

        var balls = await ComputeByGroupAsync(db, groupIds, mon);

        // Davomat: o'tilgan darslar (LessonNote.Conducted) va sababli qoldirilganlar (kech kelish emas).
        // OY tanlanganda surat ham, maxraj ham AYNAN o'sha oydan olinadi — aks holda "shu oy
        // qoldirgan / barcha vaqt o'tilgan darslar" degan ma'nosiz nisbat chiqardi.
        var lateIds = (await db.AbsenceReasons.AsNoTracking().Where(r => r.IsLate).Select(r => r.Id).ToListAsync())
            .ToHashSet();
        var notesQuery = db.LessonNotes.AsNoTracking()
            .Where(n => groupIds.Contains(n.ClassId) && n.Conducted);
        if (mon is not null) notesQuery = notesQuery.Where(n => n.Date.StartsWith(mon));
        var conductedByGroup = (await notesQuery
                .Select(n => new { n.ClassId, n.SubjectId, n.Date, n.Period })
                .ToListAsync())
            .GroupBy(n => n.ClassId)
            .ToDictionary(g => g.Key, g => g.Select(n => (n.SubjectId, n.Date, n.Period)).ToHashSet());
        var absenceQuery = db.JournalEntries.AsNoTracking()
            .Where(e => groupIds.Contains(e.ClassId) && e.ReasonId != null);
        if (mon is not null) absenceQuery = absenceQuery.Where(e => e.Date.StartsWith(mon));
        var absencesByKey = (await absenceQuery
                .Select(e => new { e.StudentId, e.ClassId, e.SubjectId, e.Date, e.Period, e.ReasonId })
                .ToListAsync())
            .Where(e => !lateIds.Contains(e.ReasonId!))
            .GroupBy(e => (e.StudentId, e.ClassId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Qator = (o'quvchi, guruh). Arxivlangan o'quvchi ro'yxatga kirmaydi.
        var raw = memberships
            .Where(m => studentName.ContainsKey(m.StudentId))
            .Select(m =>
            {
                var b = balls.GetValueOrDefault((m.StudentId, m.GroupId), new BallStat(0, 0, 0));

                int conducted = 0, absent = 0;
                if (conductedByGroup.TryGetValue(m.GroupId, out var cond))
                {
                    conducted = cond.Count;
                    if (absencesByKey.TryGetValue((m.StudentId, m.GroupId), out var abs))
                        absent = abs.Count(e => cond.Contains((e.SubjectId, e.Date, e.Period)));
                }
                double? attendance = conducted > 0
                    ? Math.Round((double)(conducted - absent) / conducted * 100)
                    : null;

                var name = groupName.GetValueOrDefault(m.GroupId, "");
                return new TeacherRatingRowDto(
                    0, m.StudentId, studentName[m.StudentId], name,
                    b.JournalTotal, b.CriteriaDone, b.Ball, b.Average, attendance,
                    m.GroupId, name);
            })
            .ToList();

        // O'RIN — GURUH ICHIDA (1,2,3...); ro'yxatning o'zi esa ball kamayishi bo'yicha.
        var rankByRow = new Dictionary<(string StudentId, string GroupId), int>();
        foreach (var g in raw.GroupBy(r => r.GroupId))
        {
            var i = 1;
            foreach (var r in g.OrderByDescending(r => r.Ball)
                         .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase))
                rankByRow[(r.StudentId, r.GroupId)] = i++;
        }

        var rows = raw
            .Select(r => r with { Rank = rankByRow[(r.StudentId, r.GroupId)] })
            .OrderByDescending(r => r.Ball)
            .ThenBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var avgBall = rows.Count > 0 ? Math.Round(rows.Average(r => (double)r.Ball), 1) : 0;
        // StudentsCount — DISTINCT o'quvchi (qatorlar soni EMAS): bir o'quvchi ikki guruhda
        // bo'lsa "o'quvchilar soni" ikki barobar ko'rinib qolardi.
        var studentsCount = rows.Select(r => r.StudentId).Distinct().Count();
        return new TeacherRatingDto(
            teacher.Id, teacher.FullName, groups.Count, studentsCount, avgBall, rows, rows.Count,
            mon ?? "", months);
    }

    /// <summary>
    /// Reyting uchun TANLASH MUMKIN bo'lgan oylar ("yyyy-MM"): eng erta ma'lumot (jurnal bahosi,
    /// mezon belgisi yoki o'tilgan dars) oyidan JORIY oygacha uzluksiz.
    /// <para>Kelajakdagi oy ATAYIN qaytarilmaydi — foydalanuvchi baribir bo'sh ekranga tushardi.
    /// Ma'lumot umuman bo'lmasa — faqat joriy oy.</para>
    /// </summary>
    private static async Task<List<string>> RatingMonthsAsync(
        IAppDbContext db, IReadOnlyCollection<string> groupIds)
    {
        var current = TuitionService.CurrentMonth();
        if (groupIds.Count == 0) return new List<string> { current };

        var minJournal = await db.JournalEntries.AsNoTracking()
            .Where(e => groupIds.Contains(e.ClassId) && e.Date != "")
            .MinAsync(e => (string?)e.Date);
        var minCriteria = await db.CriterionGrades.AsNoTracking()
            .Where(g => groupIds.Contains(g.GroupId) && g.Date != "")
            .MinAsync(g => (string?)g.Date);
        var minLesson = await db.LessonNotes.AsNoTracking()
            .Where(n => groupIds.Contains(n.ClassId) && n.Date != "")
            .MinAsync(n => (string?)n.Date);

        var start = new[] { minJournal, minCriteria, minLesson }
            .Where(d => d is { Length: >= 7 })
            .Select(d => d![..7])
            .DefaultIfEmpty(current)
            .Min()!;
        // Buzuq sana kelajakni ko'rsatib qo'ysa ham ro'yxat joriy oydan oshmaydi.
        if (string.CompareOrdinal(start, current) > 0) start = current;
        return TuitionService.MonthRange(start, current).ToList();
    }
}
