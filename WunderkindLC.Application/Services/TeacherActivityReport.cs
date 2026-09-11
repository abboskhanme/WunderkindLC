using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'qituvchilarning faollik hisobotini quradi: dars o'tyaptimi (jadvalga nisbatan bajarilish),
/// baho qo'ymoqdami, mavzu va uy vazifa bermoqdami.
///
/// <para>Jurnal yozuvlarida (LessonNote/JournalEntry) o'qituvchi id'si yo'q — kim o'qitishi dars
/// jadvalidan (ScheduleLesson.TeacherId) (ClassId, SubjectId) kaliti orqali aniqlanadi.
/// "Reja" (Expected) = haftalarga biriktirilgan jadvaldan kelib chiqib BUGUNGACHA bo'lishi kerak
/// bo'lgan dars sonidir.</para>
/// </summary>
public static class TeacherActivityReport
{
    /// <summary>Bir (o'qituvchi, guruh, fan) kesimi bo'yicha yig'ma sonlar.</summary>
    private sealed class Agg
    {
        public int Expected, Conducted, Topic, Homework, Grades;
    }

    private sealed class Computed
    {
        public Dictionary<(string Teacher, string Class, string Subject), Agg> ByKey = new();
        public Dictionary<string, string> LastActivity = new(); // teacherId -> ISO sana
    }

    /// <summary>Umumiy ko'rinish: barcha o'qituvchilar bo'yicha bitta-bitta qator + mavjud oylar ro'yxati.</summary>
    public static async Task<TeacherReportOverviewDto> BuildOverviewAsync(IAppDbContext db, string? month = null)
    {
        var (c, teachers, _, _, lifecycle, months) = await ComputeAsync(db, month);
        var rows = teachers
            .Select(t => Row(t.Id, t.FullName, t.IsArchived, KeysFor(c, t.Id), c.LastActivity.GetValueOrDefault(t.Id),
                lifecycle.GetValueOrDefault(t.Id, new LifecycleCounts())))
            .OrderBy(r => r.IsArchived).ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new TeacherReportOverviewDto(months, month ?? "", rows);
    }

    /// <summary>Bitta o'qituvchining batafsil hisoboti (guruh/fan yoyilmasi bilan).</summary>
    public static async Task<TeacherReportDetailDto?> BuildDetailAsync(IAppDbContext db, string teacherId, string? month = null)
    {
        var (c, teachers, classNames, subjectNames, lifecycle, _) = await ComputeAsync(db, month);
        var teacher = teachers.FirstOrDefault(t => t.Id == teacherId);
        if (teacher is null) return null;

        var keys = KeysFor(c, teacherId);
        var lc = lifecycle.GetValueOrDefault(teacherId, new LifecycleCounts());
        var total = Row(teacher.Id, teacher.FullName, teacher.IsArchived, keys, c.LastActivity.GetValueOrDefault(teacherId), lc);

        var breakdown = keys
            .Select(kv => new TeacherReportBreakdownDto(
                classNames.GetValueOrDefault(kv.Key.Class, kv.Key.Class),
                subjectNames.GetValueOrDefault(kv.Key.Subject, kv.Key.Subject),
                kv.Value.Expected, kv.Value.Conducted, Pct(kv.Value.Conducted, kv.Value.Expected, cap: true),
                kv.Value.Grades, Pct(kv.Value.Topic, kv.Value.Conducted), Pct(kv.Value.Homework, kv.Value.Conducted)))
            .OrderBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SubjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TeacherReportDetailDto(
            total.TeacherId, total.FullName, total.IsArchived,
            total.Expected, total.Conducted, total.DonePct,
            total.Grades, total.TopicPct, total.HomeworkPct,
            total.LastActivity, total.Status,
            total.Came, total.Active, total.Trial, total.Frozen, total.Left, total.Remaining, total.ConversionPct,
            breakdown);
    }

    // ---------- Ichki hisoblash ----------

    private static List<KeyValuePair<(string Teacher, string Class, string Subject), Agg>>
        KeysFor(Computed c, string teacherId) =>
        c.ByKey.Where(kv => kv.Key.Teacher == teacherId).ToList();

    /// <summary>Hisobot lifecycle sanoqlari — Kelgan/Faol/Sinov/Muzlat/Ketgan OQIM (shu oy),
    /// Remaining (Qolgan) = HOZIRGI aktiv (surat).</summary>
    private sealed class LifecycleCounts
    {
        public int Came, Active, Trial, Frozen, Left, Remaining;
        public int? ConversionPct => Came > 0 ? (int)Math.Round(Active * 100.0 / Came) : null;
    }

    private static TeacherReportRowDto Row(
        string teacherId, string fullName, bool isArchived,
        List<KeyValuePair<(string Teacher, string Class, string Subject), Agg>> keys,
        string? lastActivity,
        LifecycleCounts lc)
    {
        var exp = keys.Sum(k => k.Value.Expected);
        var cond = keys.Sum(k => k.Value.Conducted);
        var topic = keys.Sum(k => k.Value.Topic);
        var hw = keys.Sum(k => k.Value.Homework);
        var grades = keys.Sum(k => k.Value.Grades);

        var donePct = Pct(cond, exp, cap: true);
        // Holat: rejaga nisbatan bajarilish (reja bo'lmasa — faollik bor-yo'qligiga qarab).
        var score = exp > 0 ? donePct ?? 0 : (cond > 0 ? 100 : 0);
        var status = (exp == 0 && cond == 0 && grades == 0)
            ? "none"
            : (score >= 70 ? "active" : "low");

        return new TeacherReportRowDto(
            teacherId, fullName, isArchived,
            exp, cond, donePct, grades,
            Pct(topic, cond), Pct(hw, cond), lastActivity, status,
            lc.Came, lc.Active, lc.Trial, lc.Frozen, lc.Left, lc.Remaining, lc.ConversionPct);
    }

    /// <summary>a/b foizi (butun son). b=0 → null. cap=true bo'lsa 100 dan oshmaydi.</summary>
    private static int? Pct(int a, int b, bool cap = false)
    {
        if (b <= 0) return null;
        var p = (int)Math.Round(a * 100.0 / b);
        return cap && p > 100 ? 100 : p;
    }

    /// <summary>"yyyy-MM" oy yorlig'i bo'sh bo'lmagan ISO sanadan (uzunligi >= 7).</summary>
    private static string MonthOf(string? date) =>
        !string.IsNullOrEmpty(date) && date.Length >= 7 ? date[..7] : "";

    private static async Task<(
        Computed Computed,
        List<Teacher> Teachers,
        Dictionary<string, string> ClassNames,
        Dictionary<string, string> SubjectNames,
        Dictionary<string, LifecycleCounts> Lifecycle,
        List<string> Months)> ComputeAsync(IAppDbContext db, string? month = null)
    {
        // null/bo'sh = Umumiy (filtr yo'q)
        var filterMonth = string.IsNullOrEmpty(month) ? null : month;

        var teachers = await db.Teachers.ToListAsync();
        var classes = await db.Classes.ToListAsync();
        var classNames = classes.ToDictionary(c => c.Id, c => c.Name);
        var subjectNames = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);

        var notes = await db.LessonNotes.ToListAsync();
        var entries = await db.JournalEntries.Where(e => e.Grade != null).ToListAsync();

        // (ClassId, SubjectId=CourseId) -> TeacherId; va (ClassId, SubjectId) -> o'qituvchilar to'plami.
        // Biriktirish endi to'g'ridan-to'g'ri guruhda: Group.TeacherId (o'qituvchi) + Group.CourseId (kurs).
        var exact = new Dictionary<(string, string), string>();
        var bySubject = new Dictionary<(string, string), HashSet<string>>();
        foreach (var g in classes)
        {
            if (string.IsNullOrEmpty(g.TeacherId) || string.IsNullOrEmpty(g.CourseId)) continue;
            exact[(g.Id, g.CourseId)] = g.TeacherId;
            if (!bySubject.TryGetValue((g.Id, g.CourseId), out var set))
                bySubject[(g.Id, g.CourseId)] = set = new();
            set.Add(g.TeacherId);
        }

        string? Attribute(string classId, string subjectId)
        {
            if (exact.TryGetValue((classId, subjectId), out var t1)) return t1;
            if (bySubject.TryGetValue((classId, subjectId), out var set) && set.Count == 1) return set.First();
            return null;
        }

        var c = new Computed();
        Agg Key(string teacher, string classId, string subjectId)
        {
            var k = (teacher, classId, subjectId);
            if (!c.ByKey.TryGetValue(k, out var a)) c.ByKey[k] = a = new();
            return a;
        }
        void Touch(string teacher, string date)
        {
            if (string.IsNullOrEmpty(date)) return;
            if (!c.LastActivity.TryGetValue(teacher, out var cur) || string.CompareOrdinal(date, cur) > 0)
                c.LastActivity[teacher] = date;
        }

        // --- Reja (Expected): qo'lda qo'shilgan darslar, bugungacha (jadval olib tashlandi) ---
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        foreach (var n in notes)
        {
            if (string.CompareOrdinal(n.Date, today) > 0) continue; // kelajak dars — hali reja emas
            if (filterMonth != null && MonthOf(n.Date) != filterMonth) continue;
            var teacher = Attribute(n.ClassId, n.SubjectId);
            if (teacher is null) continue;
            Key(teacher, n.ClassId, n.SubjectId).Expected++;
        }

        // --- O'tilgan darslar + mavzu/uy vazifa (LessonNote) ---
        foreach (var n in notes)
        {
            if (!n.Conducted) continue;
            if (filterMonth != null && MonthOf(n.Date) != filterMonth) continue;
            var teacher = Attribute(n.ClassId, n.SubjectId);
            if (teacher is null) continue;
            var agg = Key(teacher, n.ClassId, n.SubjectId);
            agg.Conducted++;
            if (!string.IsNullOrWhiteSpace(n.Topic)) agg.Topic++;
            if (!string.IsNullOrWhiteSpace(n.Homework)) agg.Homework++;
            Touch(teacher, n.Date);
        }

        // --- Qo'yilgan baholar (JournalEntry) ---
        foreach (var e in entries)
        {
            if (filterMonth != null && MonthOf(e.Date) != filterMonth) continue;
            var teacher = Attribute(e.ClassId, e.SubjectId);
            if (teacher is null) continue;
            Key(teacher, e.ClassId, e.SubjectId).Grades++;
            Touch(teacher, e.Date);
        }

        // --- O'quvchi lifecycle sanoqlari: har o'qituvchi guruhlari bo'yicha ---
        // Arxivlanmagan guruhlar (active/full) -> TeacherId xaritasi
        var groupTeacher = classes
            .Where(g => !g.IsArchived && !string.IsNullOrEmpty(g.TeacherId))
            .ToDictionary(g => g.Id, g => g.TeacherId);

        // Guruh IDlari to'plami (arxivlanmagan)
        var nonArchivedGroupIds = groupTeacher.Keys.ToHashSet();

        // Barcha a'zoliklar (faqat arxivlanmagan guruhlar bo'yicha)
        var memberships = await db.StudentGroups
            .Where(sg => nonArchivedGroupIds.Contains(sg.GroupId))
            .ToListAsync();

        // --- Lifecycle: OQIM (shu oy) + QOLGAN (joriy aktiv) ---
        // Kelgan/Faol/Sinov/Muzlatilgan/Ketgan — TANLANGAN OYDAGI hodisalar (Umumiy = barcha oylar):
        //   Kelgan (Came)   = JoinedAt oyi   | Faol (Active) = ActivatedAt oyi (shu oyda aktivlashganlar)
        //   Muzlat (Frozen) = FrozenAt oyi   | Ketgan (Left) = LeftAt oyi
        //   Sinov  (Trial)  = max(0, Came − Active) (qo'shilib hali aktivlashmaganlar)
        // Qolgan (Remaining) — HOZIRGI aktiv o'quvchilar (barcha guruhlarida, oy filtriga BOG'LIQ EMAS);
        //   o'qituvchi profili/performance dagi "Faol" bilan AYNAN bir xil (MembershipLifecycle helperi).
        var lifecycle = new Dictionary<string, LifecycleCounts>();
        var byTeacher = new Dictionary<string, List<(string Status, bool IsActive, string? LeftAt)>>();
        LifecycleCounts Lc(string tId)
        {
            if (!lifecycle.TryGetValue(tId, out var lc)) lifecycle[tId] = lc = new LifecycleCounts();
            return lc;
        }
        foreach (var sg in memberships)
        {
            if (!groupTeacher.TryGetValue(sg.GroupId, out var tId)) continue;
            var lc = Lc(tId);

            // OQIM (event oyi bo'yicha)
            var joinedMonth = MonthOf(sg.JoinedAt);
            if (filterMonth == null || joinedMonth == filterMonth) lc.Came++;
            // Aktivlashish — yopilgan davrlar ham hisobga olinadi (qayta aktivlashtirilgan a'zolikda
            // joriy ActivatedAt eski hodisani ustidan yozib yuborardi).
            if (filterMonth == null
                ? MembershipLifecycle.FirstActivatedAt(sg).Length >= 7
                : MembershipLifecycle.ActivatedInMonth(sg, filterMonth)) lc.Active++;
            var frozenMonth = MonthOf(sg.FrozenAt);
            if (frozenMonth != "" && (filterMonth == null || frozenMonth == filterMonth)) lc.Frozen++;
            var leftMonth = MonthOf(sg.LeftAt);
            if (leftMonth != "" && (filterMonth == null || leftMonth == filterMonth)) lc.Left++;

            // QOLGAN uchun a'zoliklarni yig'amiz (joriy suratkash)
            if (!byTeacher.TryGetValue(tId, out var list)) byTeacher[tId] = list = new();
            list.Add((sg.Status, sg.IsActive, sg.LeftAt));
        }
        foreach (var lc in lifecycle.Values) lc.Trial = Math.Max(0, lc.Came - lc.Active);
        // Qolgan = hozirgi aktiv (performance bilan bir xil ta'rif)
        foreach (var (tId, list) in byTeacher) Lc(tId).Remaining = MembershipLifecycle.Tally(list).Active;

        // --- Mavjud oylar ro'yxati (uzluksiz, yillar bo'ylab) ---
        var monthCandidates = new List<string>();
        monthCandidates.AddRange(memberships.Select(sg => MonthOf(sg.JoinedAt)));
        monthCandidates.AddRange(notes.Select(n => MonthOf(n.Date)));
        monthCandidates.AddRange(entries.Select(e => MonthOf(e.Date)));
        var nonEmpty = monthCandidates.Where(m => m != "").ToList();
        var startMonth = nonEmpty.Count > 0
            ? nonEmpty.Min()!
            : await TuitionService.AcademicYearStartMonthAsync(db);
        var months = TuitionService.MonthRange(startMonth, TuitionService.CurrentMonth()).ToList();

        return (c, teachers, classNames, subjectNames, lifecycle, months);
    }
}
