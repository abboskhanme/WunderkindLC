using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Crm;

/// <summary>
/// U3a — ONE attendance definition (SPEC P3.1): <see cref="AttendanceRules"/>, the "attendance taken"
/// stamps (<see cref="AttendanceTaking"/>, <see cref="JournalService"/>) and the excused flag.
/// </summary>
public class AttendanceRulesTests
{
    private static readonly AbsenceReason Late = new() { Name = "Kech keldi", IsLate = true };
    private static readonly AbsenceReason Ill = new() { Name = "Kasal", IsExcused = true };
    private static readonly AbsenceReason NoReason = new() { Name = "Sababsiz" };

    private static LessonMark C(bool conducted = true, bool taken = false, bool blocked = false, bool beforeDefault = false,
        bool present = false, int? grade = null, bool mastery = false, AbsenceReason? reason = null) =>
        AttendanceRules.Classify(conducted, taken, blocked, beforeDefault, present, grade, mastery, reason);

    /* =========================================================================================
     *  1) Classify — the order of the rule
     * ========================================================================================= */

    [Fact]
    public void Late_is_attended_and_held()
    {
        var m = C(reason: Late);
        Assert.Equal(LessonMark.Late, m);
        Assert.True(AttendanceRules.IsAttended(m));
        Assert.True(AttendanceRules.IsHeld(m));
    }

    [Fact]
    public void Graded_or_mastery_or_explicit_present_is_present_even_without_attendance_taken()
    {
        Assert.Equal(LessonMark.Present, C(grade: 5));
        Assert.Equal(LessonMark.Present, C(mastery: true));
        Assert.Equal(LessonMark.Present, C(present: true));
        // Explicit «keldi» before PresentDefaultFrom is still present (only the DEFAULT is restricted).
        Assert.Equal(LessonMark.Present, C(present: true, beforeDefault: true));
    }

    [Fact]
    public void Attendance_taken_and_no_mark_is_present()
    {
        Assert.Equal(LessonMark.Present, C(taken: true));
    }

    [Fact]
    public void Conducted_only_and_no_mark_is_unmarked_and_excluded()
    {
        var m = C(taken: false);
        Assert.Equal(LessonMark.Unmarked, m);
        Assert.False(AttendanceRules.IsHeld(m));
    }

    [Fact]
    public void Before_PresentDefaultFrom_no_mark_is_unmarked_even_when_attendance_taken()
    {
        Assert.Equal(LessonMark.Unmarked, C(taken: true, beforeDefault: true));
    }

    [Fact]
    public void Excused_vs_unexcused()
    {
        Assert.Equal(LessonMark.AbsentExcused, C(reason: Ill));
        Assert.Equal(LessonMark.AbsentUnexcused, C(reason: NoReason));
        // A reason wins over a grade (late + grade is still late = attended).
        Assert.Equal(LessonMark.Late, C(reason: Late, grade: 4));
    }

    [Fact]
    public void Blocked_wins_over_everything()
    {
        Assert.Equal(LessonMark.Blocked, C(blocked: true, reason: NoReason, present: true, taken: true));
    }

    [Fact]
    public void Not_conducted_and_no_mark_is_not_held()
    {
        Assert.Equal(LessonMark.NotHeld, C(conducted: false, taken: true));
    }

    /* =========================================================================================
     *  2) Tally
     * ========================================================================================= */

    [Fact]
    public void Tally_counts_late_as_attended_and_excludes_unmarked()
    {
        var t = AttendanceRules.Tally([
            LessonMark.Present, LessonMark.Late, LessonMark.AbsentExcused, LessonMark.AbsentUnexcused,
            LessonMark.Unmarked, LessonMark.NotHeld, LessonMark.Blocked,
        ]);
        Assert.Equal(4, t.Held);
        Assert.Equal(2, t.Attended);
        Assert.Equal(2, t.Absent);
        Assert.Equal(1, t.Unmarked);
        Assert.Equal(50, t.Percent);
    }

    [Fact]
    public void Tally_of_nothing_held_has_no_percent()
    {
        Assert.Null(AttendanceRules.Tally([LessonMark.Unmarked, LessonMark.NotHeld]).Percent);
        Assert.Equal(67, AttendanceRules.Tally([LessonMark.Present, LessonMark.Late, LessonMark.AbsentUnexcused]).Percent);
    }

    /* =========================================================================================
     *  3) Excused flag rules + backfill semantics
     * ========================================================================================= */

    [Fact]
    public void Late_and_excused_together_is_rejected()
    {
        Assert.NotNull(AbsenceReasonRules.ExcusedError(isLate: true, isExcused: true));
        Assert.Null(AbsenceReasonRules.ExcusedError(isLate: true, isExcused: false));
        Assert.Null(AbsenceReasonRules.ExcusedError(isLate: false, isExcused: true));
    }

    [Theory]
    [InlineData("Sababsiz", false, false)]
    [InlineData("SABABSIZ qoldirdi", false, false)]
    [InlineData("Kasal", false, true)]
    [InlineData("Sababli", false, true)]
    [InlineData("Kech keldi", true, false)]
    public void Backfill_rule_matches_the_migration_sql(string name, bool isLate, bool expectedExcused)
    {
        Assert.Equal(expectedExcused, AbsenceReasonRules.DefaultExcused(name, isLate));
    }

    [Fact]
    public async Task Excused_flag_may_change_on_a_used_reason_but_late_flag_may_not()
    {
        using var db = TestDb.Sqlite();
        var r = new AbsenceReason { Name = "Kasal", Short = "K" };
        db.Context.AbsenceReasons.Add(r);
        db.Context.JournalEntries.Add(new JournalEntry { ClassId = "g", SubjectId = "s", StudentId = "st", Date = "2026-09-01", ReasonId = r.Id });
        db.Context.SaveChanges();

        Assert.Null(await AbsenceReasonRules.ValidateReplaceAsync(db.Context,
            [new AbsenceReasonDto(r.Id, "Kasal", "K", false, IsExcused: true)]));
        Assert.NotNull(await AbsenceReasonRules.ValidateReplaceAsync(db.Context,
            [new AbsenceReasonDto(r.Id, "Kasal", "K", true)]));
    }
}
