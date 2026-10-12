using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>How ONE student stood in ONE lesson — the single attendance vocabulary (SPEC P3.1).</summary>
public enum LessonMark
{
    /// <summary>The lesson was not conducted and nothing was marked — not a lesson for statistics.</summary>
    NotHeld,
    /// <summary>Outside the membership (before the group/membership start, after freeze/leave).</summary>
    Blocked,
    /// <summary>Conducted, but attendance was never taken for this student — neither present nor absent.</summary>
    Unmarked,
    Present,
    /// <summary>Late arrival — ATTENDED (counts as present in every percentage).</summary>
    Late,
    AbsentExcused,
    AbsentUnexcused,
}

/// <summary>
/// ONE attendance definition for the whole system (SPEC P3.1, <c>.claude/rules/journal.md</c>):
/// <list type="bullet">
///   <item><b>late = attended</b>;</item>
///   <item>graded, mastery-marked or explicit «keldi» = present;</item>
///   <item>attendance TAKEN (lesson complete) + no mark = present (only from <c>PresentDefaultFrom</c>);</item>
///   <item>conducted only + no mark = <see cref="LessonMark.Unmarked"/> — EXCLUDED from the percentage.</item>
/// </list>
/// Every profile / portal / teacher / analytics count goes through <see cref="Classify(bool,bool,bool,bool,JournalEntry?,AbsenceReason?)"/>
/// and <see cref="Tally"/> — never re-derive "absent = reason that is not late" by hand.
/// </summary>
public static class AttendanceRules
{
    /// <summary>
    /// Classifies one (student, lesson). Order (SPEC): blocked → reason (late / excused / unexcused) →
    /// explicit mark (present, grade, mastery) → not conducted → attendance taken (and on/after
    /// <c>PresentDefaultFrom</c>) → unmarked.
    /// </summary>
    /// <param name="conducted">The lesson is marked conducted («o'tildi»).</param>
    /// <param name="attendanceTaken">Attendance of the lesson is COMPLETE (<see cref="LessonNote.AttendanceTaken"/>).</param>
    /// <param name="blocked">The date is outside the membership/group bounds.</param>
    /// <param name="beforePresentDefault">The date is before <c>StudentGroup.RecordedAt</c> (back-dated membership).</param>
    public static LessonMark Classify(
        bool conducted, bool attendanceTaken, bool blocked, bool beforePresentDefault,
        JournalEntry? e, AbsenceReason? reason) =>
        Classify(conducted, attendanceTaken, blocked, beforePresentDefault,
            e?.Present == true, e?.Grade, e?.Mastery is not null, reason);

    /// <summary>Same rule on raw fields (DTO rows, projections).</summary>
    public static LessonMark Classify(
        bool conducted, bool attendanceTaken, bool blocked, bool beforePresentDefault,
        bool present, int? grade, bool hasMastery, AbsenceReason? reason)
    {
        if (blocked) return LessonMark.Blocked;
        if (reason is not null)
            return reason.IsLate ? LessonMark.Late
                : reason.IsExcused ? LessonMark.AbsentExcused
                : LessonMark.AbsentUnexcused;
        if (present || grade is not null || hasMastery) return LessonMark.Present;
        if (!conducted) return LessonMark.NotHeld;
        if (attendanceTaken && !beforePresentDefault) return LessonMark.Present;
        return LessonMark.Unmarked;
    }

    /// <summary>Counts in Held (the percentage denominator): present, late, excused, unexcused.</summary>
    public static bool IsHeld(LessonMark m) =>
        m is LessonMark.Present or LessonMark.Late or LessonMark.AbsentExcused or LessonMark.AbsentUnexcused;

    /// <summary>Attended = present or late.</summary>
    public static bool IsAttended(LessonMark m) => m is LessonMark.Present or LessonMark.Late;

    /// <summary>Absent (excused or not).</summary>
    public static bool IsAbsent(LessonMark m) => m is LessonMark.AbsentExcused or LessonMark.AbsentUnexcused;

    /// <summary>
    /// Aggregate of marks. <see cref="Held"/> = Present + Late + AbsentExcused + AbsentUnexcused;
    /// <see cref="Attended"/> = Present + Late; <see cref="Percent"/> = round(Attended × 100 / Held),
    /// <c>null</c> when nothing was held. Unmarked / NotHeld / Blocked are excluded.
    /// </summary>
    public sealed record AttendanceTally(
        int Present, int Late, int AbsentExcused, int AbsentUnexcused, int Unmarked)
    {
        public int Held => Present + Late + AbsentExcused + AbsentUnexcused;
        public int Attended => Present + Late;
        public int Absent => AbsentExcused + AbsentUnexcused;
        public int? Percent => Held == 0 ? null : PercentOf(Attended, Held);
    }

    public static AttendanceTally Tally(IEnumerable<LessonMark> marks)
    {
        int present = 0, late = 0, exc = 0, unexc = 0, unmarked = 0;
        foreach (var m in marks)
        {
            switch (m)
            {
                case LessonMark.Present: present++; break;
                case LessonMark.Late: late++; break;
                case LessonMark.AbsentExcused: exc++; break;
                case LessonMark.AbsentUnexcused: unexc++; break;
                case LessonMark.Unmarked: unmarked++; break;
            }
        }
        return new AttendanceTally(present, late, exc, unexc, unmarked);
    }

    /// <summary>round(attended × 100 / held), half away from zero (the same rounding everywhere).</summary>
    public static int PercentOf(int attended, int held) =>
        held <= 0 ? 0 : (int)Math.Round(attended * 100.0 / held, MidpointRounding.AwayFromZero);

    /// <summary>"yyyy-MM-dd" &lt; presentDefaultFrom (null = no restriction).</summary>
    public static bool BeforePresentDefault(string date, string? presentDefaultFrom) =>
        presentDefaultFrom is not null && string.CompareOrdinal(date, presentDefaultFrom) < 0;

    /// <summary>Lowercase mark key used in API payloads: present · late · absent_excused · absent_unexcused · unmarked.</summary>
    public static string Key(LessonMark m) => m switch
    {
        LessonMark.Present => "present",
        LessonMark.Late => "late",
        LessonMark.AbsentExcused => "absent_excused",
        LessonMark.AbsentUnexcused => "absent_unexcused",
        LessonMark.Unmarked => "unmarked",
        LessonMark.Blocked => "blocked",
        _ => "not_held",
    };
}
