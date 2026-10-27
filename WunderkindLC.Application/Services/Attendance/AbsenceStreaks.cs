using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KETMA-KET SABABSIZ KELMASLIK (SPEC P3.5, <c>AbsenceStreaksTests</c>) — bitta (o'quvchi, guruh) bo'yicha.
/// Eng oxirgi darsdan ORQAGA yuriladi: sababsiz yo'qlik sanaladi, belgilanmagan / o'tilmagan / a'zolikdan
/// tashqari dars O'TKAZIB YUBORILADI, «keldi», «kech» yoki «sababli» yo'qlik seriyani TO'XTATADI. Turli guruhlar
/// hech qachon aralashmaydi.
/// </summary>
public static class AbsenceStreaks
{
    public const int Level3 = 3;
    public const int Level5 = 5;

    /// <summary>Seriya: sababsiz qoldirilgan darslar soni, birinchi (eng eski) sanasi va sanalari (o'sish tartibida).</summary>
    public sealed record Streak(int Count, string StartDate, IReadOnlyList<string> Dates)
    {
        public string LastDate => Dates.Count == 0 ? "" : Dates[^1];
    }

    public static readonly Streak None = new(0, "", []);

    /// <param name="marks">Bitta (o'quvchi, guruh) darslari, SANA bo'yicha O'SISH tartibida.</param>
    public static Streak Compute(IReadOnlyList<(string Date, LessonMark Mark)> marks)
    {
        var dates = new List<string>();
        for (var i = marks.Count - 1; i >= 0; i--)
        {
            var m = marks[i].Mark;
            if (m == LessonMark.AbsentUnexcused) { dates.Add(marks[i].Date); continue; }
            if (m is LessonMark.Present or LessonMark.Late or LessonMark.AbsentExcused) break;
            // Unmarked / NotHeld / Blocked — o'tkazib yuboriladi.
        }
        if (dates.Count == 0) return None;
        dates.Reverse();
        return new Streak(dates.Count, dates[0], dates);
    }

    /// <summary>5 — 5+ dars, 3 — 3..4 dars, aks holda 0 (ogohlantirish yo'q). Qizil ro'yxat, KPI va MCP darajasi.</summary>
    public static int LevelOf(int count) => count >= Level5 ? Level5 : count >= Level3 ? Level3 : 0;

    /// <summary>
    /// «BOG'LANISH KERAK» chegarasi — SOZLAMA (<see cref="CenterMeta.ContactAbsenceLessons"/>, «Sozlamalar → Servis»;
    /// egasining talabi, 2026-10-27). ⚠️ Qizil ro'yxat/KPI darajasi (<see cref="LevelOf"/>, 3/5) ATAYIN bog'lanmagan —
    /// "Ketma-ket 3 dars kelmaganlar" KPI ko'rsatkichi o'z ma'nosida qolsin.
    /// </summary>
    public const int DefaultContactLessons = 3;
    public const int MinContactLessons = 1;
    public const int MaxContactLessons = 10;

    public static int ClampContactLessons(int n) => n is >= MinContactLessons and <= MaxContactLessons ? n : DefaultContactLessons;

    /// <summary>Darajalar: sozlangan chegarada talab, 5 da (chegara 5 dan kichik bo'lsa) — izoh yoki yangi talab.</summary>
    public static IReadOnlyList<int> ContactLevels(int start)
    {
        var s = ClampContactLessons(start);
        return s < Level5 ? [s, Level5] : [s];
    }

    /// <summary>
    /// Shu seriya uchun YANGI ko'tariladigan bog'lanish darajalari: <paramref name="count"/> ga yetgan va seriyada allaqachon
    /// yozilgan ENG YUQORI darajadan (<paramref name="maxRaised"/>, 0 = hech biri) baland. Shu sababli chegara keyin
    /// PASAYTIRILSA (3 → 2) allaqachon ishlangan seriya qayta talab ochmaydi.
    /// </summary>
    public static IReadOnlyList<int> ContactLevelsToRaise(int count, int maxRaised, int start) =>
        ContactLevels(start).Where(l => l <= count && l > maxRaised).ToList();

    /// <summary>«Bog'lanish kerak» izohi: «{guruh}: ketma-ket {n} ta dars sababsiz qoldirildi (dd.MM, dd.MM, …)».</summary>
    public static string NoteText(string groupName, Streak s) =>
        $"{groupName}: ketma-ket {s.Count} ta dars sababsiz qoldirildi ("
        + string.Join(", ", s.Dates.Select(d => d.Length >= 10 ? $"{d[8..10]}.{d[5..7]}" : d)) + ")";

    /// <summary>Avtomatik talab FAQAT yaqindagi seriyaga: oxirgi yo'qlik ≤ 14 kun oldin (deploydan keyin eski
    /// seriyalar to'lqini bo'lmasin).</summary>
    public const int FreshDays = 14;

    public static bool IsFresh(Streak s, DateOnly today) =>
        DateOnly.TryParse(s.LastDate, out var last) && today.DayNumber - last.DayNumber <= FreshDays;
}

/// <summary>
/// Barcha (o'quvchi, guruh) seriyalarini jurnaldan JONLI hisoblaydi — batch (har jadvalga bitta so'rov). Qizil
/// ro'yxat, KPI va fon xizmati AYNAN shu funksiyani chaqiradi. Klassifikatsiya — <see cref="AttendanceRules"/>
/// (profil bilan bir xil: <see cref="StudentJournalBuilder.BoundsOf"/> chegaralari, <c>PresentDefaultFrom</c>).
/// </summary>
public static class AbsenceStreakLoader
{
    /// <summary>Qancha kun orqaga qaraladi (SPEC P3.5: 60 kun).</summary>
    public const int LookbackDays = 60;

    public sealed record Row(
        string StudentId, string StudentName, string GroupId, string GroupName, string TeacherId,
        AbsenceStreaks.Streak Streak)
    {
        public int Level => AbsenceStreaks.LevelOf(Streak.Count);
    }

    /// <summary>
    /// Arxivlanmagan, kursi bor guruhlarning TIRIK a'zoliklari (arxivlanmagan o'quvchilar) bo'yicha seriyalar;
    /// faqat Count ≥ 1 bo'lganlari qaytadi. <paramref name="asOf"/> — shu kungacha (shu kun ham) darslar.
    /// </summary>
    public static async Task<List<Row>> ComputeAsync(IAppDbContext db, DateOnly asOf, CancellationToken ct = default)
    {
        var fromIso = asOf.AddDays(-LookbackDays).ToString("yyyy-MM-dd");
        var toIso = asOf.ToString("yyyy-MM-dd");

        var groups = await db.Classes.AsNoTracking()
            .Where(g => !g.IsArchived && g.CourseId != null && g.CourseId != "")
            .Select(g => new { g.Id, g.Name, g.CourseId, g.StartDate, g.TeacherId })
            .ToListAsync(ct);
        if (groups.Count == 0) return [];
        var gids = groups.Select(g => g.Id).ToList();
        var groupById = groups.ToDictionary(g => g.Id, StringComparer.Ordinal);

        var notes = await db.LessonNotes.AsNoTracking()
            .Where(n => gids.Contains(n.ClassId) && n.Conducted
                        && string.Compare(n.Date, fromIso) >= 0 && string.Compare(n.Date, toIso) <= 0)
            .Select(n => new { n.ClassId, n.SubjectId, n.Date, n.AttendanceTaken })
            .ToListAsync(ct);
        // (guruh) → sana → davomat TO'LIQ olinganmi (faqat guruh kursi bo'yicha — jurnal bilan bir xil).
        var lessons = notes
            .Where(n => groupById[n.ClassId].CourseId == n.SubjectId)
            .GroupBy(n => n.ClassId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(n => n.Date, StringComparer.Ordinal)
                    .Select(d => (Date: d.Key, Taken: d.Any(x => x.AttendanceTaken)))
                    .OrderBy(d => d.Date, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);
        if (lessons.Count == 0) return [];

        var lessonGroupIds = lessons.Keys.ToList();
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.IsActive && lessonGroupIds.Contains(sg.GroupId))
            .ToListAsync(ct);
        var studentIds = memberships.Select(m => m.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && !s.IsArchived)
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => lessonGroupIds.Contains(e.ClassId)
                        && string.Compare(e.Date, fromIso) >= 0 && string.Compare(e.Date, toIso) <= 0
                        && (e.ReasonId != null || e.Present || e.Grade != null || e.Mastery != null))
            .Select(e => new { e.ClassId, e.SubjectId, e.StudentId, e.Date, e.ReasonId, e.Present, e.Grade, e.Mastery })
            .ToListAsync(ct);
        var entryByKey = entries
            .Where(e => groupById[e.ClassId].CourseId == e.SubjectId)
            .GroupBy(e => (e.StudentId, e.ClassId, e.Date))
            .ToDictionary(g => g.Key, g => g.First());
        var reasons = await db.AbsenceReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);

        var result = new List<Row>();
        foreach (var m in memberships)
        {
            if (!students.TryGetValue(m.StudentId, out var studentName)) continue;   // arxivlangan
            var g = groupById[m.GroupId];
            var bounds = StudentJournalBuilder.BoundsOf(m);
            var groupStart = g.StartDate is { Length: >= 10 } ? g.StartDate[..10] : null;
            var marks = new List<(string, LessonMark)>();
            foreach (var (date, taken) in lessons[m.GroupId])
            {
                var blocked = (groupStart is not null && string.CompareOrdinal(date, groupStart) < 0)
                    || (bounds.MemberStart is not null && string.CompareOrdinal(date, bounds.MemberStart) < 0)
                    || (bounds.MemberEnd is not null && string.CompareOrdinal(date, bounds.MemberEnd) > 0);
                var e = entryByKey.GetValueOrDefault((m.StudentId, m.GroupId, date));
                var reason = e?.ReasonId is not null ? reasons.GetValueOrDefault(e.ReasonId) : null;
                marks.Add((date, AttendanceRules.Classify(true, taken, blocked,
                    AttendanceRules.BeforePresentDefault(date, bounds.PresentDefaultFrom),
                    e?.Present == true, e?.Grade, e?.Mastery is not null, reason)));
            }
            var streak = AbsenceStreaks.Compute(marks);
            if (streak.Count == 0) continue;
            result.Add(new Row(m.StudentId, studentName, m.GroupId, g.Name, g.TeacherId ?? "", streak));
        }
        return result;
    }
}
