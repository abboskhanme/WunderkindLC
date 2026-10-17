using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «DAVR TAHLILI» — O'quvchilar davomati sahifasining tabi (SPEC-backlog-2026-10-17 A1, TZ 2.1): tanlangan davr
/// (ko'pi bilan <see cref="MaxDays"/> kun) bo'yicha keldi · kech · sababli · sababsiz; guruhlar kesimida OLDINGI teng
/// davr bilan solishtirish (davomati <see cref="DropPp"/> foiz punktdan ko'p tushgan guruhlar); sabablar statistikasi;
/// maktab sinflari kesimi; davrda jami 3+ / 5+ dars qoldirganlar.
///
/// <para>Sanoqlar FAQAT <see cref="MembershipAttendance.ComputeAsync"/> dan — YAGONA davomat ta'rifi (dars monitoringi,
/// reyting, bosh sahifa bilan bir xil). ⚠️ U faqat TIRIK a'zoliklarni sanaydi: davr ichida guruhdan chiqib ketgan
/// o'quvchi bu hisobotda yo'q (agregat hisobotlarning umumiy qoidasi).</para>
/// </summary>
public static class AttendancePeriodReport
{
    /// <summary>Eng uzun davr (TZ: 92 kungacha).</summary>
    public const int MaxDays = 92;

    /// <summary>Standart davr — oxirgi 30 kun.</summary>
    public const int DefaultDays = 30;

    /// <summary>"Davomat pasaydi" — oldingi teng davrga nisbatan shuncha foiz punkt va undan ko'p tushish.</summary>
    public const int DropPp = 10;

    /// <summary>Xavf ro'yxatiga tushish chegarasi — davrda jami qoldirilgan darslar.</summary>
    public const int RiskMin = 3;

    /// <summary>Davrni tekshiradi. Xato bo'lsa — foydalanuvchiga ko'rsatiladigan sabab.</summary>
    public static string? Validate(string? from, string? to, DateOnly today, out DateOnly f, out DateOnly t)
    {
        t = DateOnly.TryParseExact(to ?? "", "yyyy-MM-dd", out var tt) ? tt : today;
        f = DateOnly.TryParseExact(from ?? "", "yyyy-MM-dd", out var ff) ? ff : t.AddDays(-(DefaultDays - 1));
        if (f > t) return "Davr boshi oxiridan keyin bo'lishi mumkin emas";
        if (t.DayNumber - f.DayNumber + 1 > MaxDays) return $"Davr {MaxDays} kundan uzun bo'lmasin";
        return null;
    }

    public static AttendanceCountsDto Counts(MembershipAttendance.Stat s) =>
        new(s.Held, s.Present, s.Late, s.AbsentExcused, s.AbsentUnexcused, s.Percent);

    /// <summary>Foiz punkt farqi (ikkala davrda ham dars bo'lgan bo'lsa).</summary>
    public static int? Delta(int? current, int? previous) =>
        current is int c && previous is int p ? c - p : null;

    public static bool IsDrop(int? delta) => delta is int d && d <= -DropPp;

    public static string GradeLabel(int? grade) => grade switch
    {
        null => "Sinf kiritilmagan",
        SchoolGrades.Graduated => "Maktabni bitirgan",
        _ => $"{grade}-sinf",
    };

    public static async Task<AttendancePeriodDto> BuildAsync(
        IAppDbContext db, DateOnly from, DateOnly to, string? groupId, string? teacherId, DateOnly today,
        CancellationToken ct = default)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        var prevTo = from.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(days - 1));
        static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

        // Guruh qamrovi (o'qituvchi — guruhning HOZIRGI o'qituvchisi).
        var groups = await db.Classes.AsNoTracking()
            .Select(g => new { g.Id, g.Name, g.CourseId, g.TeacherId }).ToListAsync(ct);
        var scope = groups
            .Where(g => (string.IsNullOrEmpty(groupId) || g.Id == groupId)
                        && (string.IsNullOrEmpty(teacherId) || g.TeacherId == teacherId))
            .ToList();
        var scopeIds = scope.Select(g => g.Id).ToList();

        var cur = await MembershipAttendance.ComputeAsync(db, scopeIds, fromDay: D(from), toDay: D(to), ct: ct);
        var prev = await MembershipAttendance.ComputeAsync(db, scopeIds, fromDay: D(prevFrom), toDay: D(prevTo), ct: ct);

        var studentIds = cur.Keys.Select(k => k.StudentId).Concat(prev.Keys.Select(k => k.StudentId))
            .ToHashSet(StringComparer.Ordinal);
        var students = (await db.Students.AsNoTracking()
                .Select(s => new
                {
                    s.Id, s.FullName, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone,
                    s.SchoolGrade, s.SchoolGradeYear, s.IsArchived,
                })
                .ToListAsync(ct))
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionary(s => s.Id, StringComparer.Ordinal);
        // Arxivdagi o'quvchi "hozirgi o'quvchi" emas (eski ma'lumotda a'zoligi tirik bo'lishi mumkin).
        bool Live(string sid) => students.TryGetValue(sid, out var s) && !s.IsArchived;
        var curLive = cur.Where(kv => Live(kv.Key.StudentId)).ToList();
        var prevLive = prev.Where(kv => Live(kv.Key.StudentId)).ToList();

        static MembershipAttendance.Stat Sum(IEnumerable<MembershipAttendance.Stat> xs) =>
            xs.Aggregate(MembershipAttendance.Stat.Empty, (a, b) => a.Plus(b));

        var courseNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var teacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName, ct);

        // ---- guruhlar: joriy va oldingi davr ----
        var groupRows = new List<AttendancePeriodGroupDto>();
        foreach (var g in scope)
        {
            var c = Sum(curLive.Where(kv => kv.Key.GroupId == g.Id).Select(kv => kv.Value));
            var p = Sum(prevLive.Where(kv => kv.Key.GroupId == g.Id).Select(kv => kv.Value));
            if (c.Held == 0 && p.Held == 0) continue;
            var delta = Delta(c.Percent, p.Percent);
            groupRows.Add(new AttendancePeriodGroupDto(
                g.Id, g.Name, courseNames.GetValueOrDefault(g.CourseId, ""), teacherNames.GetValueOrDefault(g.TeacherId, ""),
                curLive.Count(kv => kv.Key.GroupId == g.Id && kv.Value.Held > 0),
                Counts(c), Counts(p), delta, IsDrop(delta)));
        }
        groupRows = groupRows
            .OrderBy(r => r.DeltaPp ?? int.MaxValue)
            .ThenBy(r => r.Current.Percent ?? 101)
            .ThenBy(r => r.GroupName, StringComparer.CurrentCulture)
            .ToList();

        // ---- o'quvchi bo'yicha (sinf kesimi va xavf ro'yxati) ----
        var byStudent = curLive.GroupBy(kv => kv.Key.StudentId)
            .ToDictionary(g => g.Key, g => (Stat: Sum(g.Select(x => x.Value)), Groups: g.Select(x => x.Key.GroupId).ToList()));
        var groupName = groups.ToDictionary(g => g.Id, g => g.Name, StringComparer.Ordinal);

        var grades = byStudent
            .Where(kv => kv.Value.Stat.Held > 0)
            .GroupBy(kv =>
            {
                var s = students[kv.Key];
                return SchoolGrades.Current(s.SchoolGrade, s.SchoolGradeYear, today);
            })
            .Select(g => new AttendanceGradeStatDto(g.Key, GradeLabel(g.Key), g.Count(), Counts(Sum(g.Select(x => x.Value.Stat)))))
            .OrderBy(g => g.Grade ?? int.MaxValue)
            .ToList();

        var atRisk = byStudent
            .Where(kv => kv.Value.Stat.Absent >= RiskMin)
            .Select(kv =>
            {
                var s = students[kv.Key];
                var st = kv.Value.Stat;
                return new AttendanceRiskStudentDto(
                    kv.Key, s.FullName, DebtReport.FirstPhone(s.ParentPhone, s.FatherPhone, s.MotherPhone, s.Phone),
                    kv.Value.Groups.Select(id => groupName.GetValueOrDefault(id, "—")).Distinct().ToList(),
                    st.Held, st.Absent, st.AbsentExcused, st.AbsentUnexcused, st.Late, st.Percent);
            })
            .OrderByDescending(r => r.Absent).ThenByDescending(r => r.Unexcused)
            .ThenBy(r => r.StudentName, StringComparer.CurrentCulture)
            .ToList();

        // ---- sabablar: davrdagi O'TILGAN darslarda qo'yilgan sabab kataklari (har katak bir marta) ----
        var pairs = curLive.Select(kv => kv.Key).ToHashSet();
        var fd = D(from);
        var td = D(to);
        var reasonCells = await db.JournalEntries.AsNoTracking()
            .Where(e => e.ReasonId != null && scopeIds.Contains(e.ClassId)
                        && string.Compare(e.Date, fd) >= 0 && string.Compare(e.Date, td) <= 0
                        && db.LessonNotes.Any(n => n.Conducted && n.ClassId == e.ClassId && n.SubjectId == e.SubjectId
                                                   && n.Date == e.Date && n.Period == e.Period))
            .Select(e => new { e.StudentId, e.ClassId, e.SubjectId, e.Date, e.Period, e.ReasonId })
            .Distinct()
            .ToListAsync(ct);
        var reasons = await db.AbsenceReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);
        var reasonRows = reasonCells
            .Where(x => pairs.Contains((x.StudentId, x.ClassId)))
            .GroupBy(x => x.ReasonId!)
            .Select(g =>
            {
                reasons.TryGetValue(g.Key, out var r);
                return new AttendanceReasonStatDto(
                    g.Key, r?.Name ?? "Noma'lum sabab", r?.IsExcused ?? false, r?.IsLate ?? false,
                    g.Count(), g.Select(x => x.StudentId).Distinct().Count());
            })
            .OrderByDescending(r => r.Count)
            .ToList();

        return new AttendancePeriodDto(
            fd, td, D(prevFrom), D(prevTo), days, DropPp,
            Counts(Sum(curLive.Select(kv => kv.Value))), Counts(Sum(prevLive.Select(kv => kv.Value))),
            groupRows, reasonRows, grades, atRisk);
    }
}
