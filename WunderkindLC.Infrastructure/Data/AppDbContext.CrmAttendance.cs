using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>CRM upgrade Phase 3 «Attendance» model (docs/SPEC-crm-upgrade.md §4).</summary>
public partial class AppDbContext
{
    public DbSet<TeacherAttendanceFine> TeacherAttendanceFines => Set<TeacherAttendanceFine>();
    public DbSet<AbsenceStreakAlert> AbsenceStreakAlerts => Set<AbsenceStreakAlert>();

    partial void ConfigureCrmAttendance(ModelBuilder b)
    {
        // ---- U3a: one attendance definition + settings ----
        b.Entity<LessonNote>().Property(x => x.AttendanceTakenById).HasMaxLength(200);
        // Reminders / fines / monitor look up "any note of (ClassId, Date) with AttendanceTakenAt".
        b.Entity<LessonNote>().HasIndex(x => new { x.ClassId, x.Date });

        b.Entity<CenterMeta>().Property(x => x.AttendanceReminderMinutes).HasMaxLength(32);
        b.Entity<CenterMeta>().Property(x => x.AttendanceFineAmount).HasPrecision(18, 2);
        b.Entity<CenterMeta>().Property(x => x.AbsentNoticeApproval).HasMaxLength(16);
        b.Entity<CenterMeta>().Property(x => x.AbsenceStreakReasonId).HasMaxLength(200);

        // ---- U3c: teacher attendance fines ----
        var f = b.Entity<TeacherAttendanceFine>();
        f.Property(x => x.Id).HasMaxLength(36);
        f.Property(x => x.TeacherId).HasMaxLength(36);
        f.Property(x => x.GroupId).HasMaxLength(36);
        f.Property(x => x.LessonDate).HasMaxLength(10);
        f.Property(x => x.Month).HasMaxLength(7);
        f.Property(x => x.LessonStart).HasMaxLength(5);
        f.Property(x => x.Amount).HasPrecision(18, 2);
        f.Property(x => x.Status).HasMaxLength(16);
        f.Property(x => x.WaivedById).HasMaxLength(200);
        f.Property(x => x.WaivedByName).HasMaxLength(200);
        f.Property(x => x.WaiveReason).HasMaxLength(300);
        // At most one fine per group per day — the background job relies on it (restart/race safe).
        f.HasIndex(x => new { x.GroupId, x.LessonDate }).IsUnique();
        f.HasIndex(x => new { x.TeacherId, x.Month });
        f.HasIndex(x => x.LessonDate);

        // ---- U3d: consecutive-absence streak alerts ----
        var a = b.Entity<AbsenceStreakAlert>();
        a.Property(x => x.StudentId).HasMaxLength(200);
        a.Property(x => x.GroupId).HasMaxLength(200);
        a.Property(x => x.StreakStart).HasMaxLength(10);
        a.Property(x => x.ContactRequestId).HasMaxLength(200);
        a.HasIndex(x => new { x.StudentId, x.GroupId, x.StreakStart, x.Level }).IsUnique();

        // ---- U3b: absent-notice queue with manager hold (columns on AttendanceNotifyItem) ----
        var n = b.Entity<AttendanceNotifyItem>();
        n.Property(x => x.LessonStart).HasMaxLength(5);
        n.Property(x => x.LessonEnd).HasMaxLength(5);
        n.Property(x => x.DecidedById).HasMaxLength(200);
        n.Property(x => x.DecidedByName).HasMaxLength(200);
        n.Property(x => x.DecisionNote).HasMaxLength(300);
        n.Property(x => x.CancelReason).HasMaxLength(32);
        // Worker: "(pending|approved) && SendAfter <= now"; control page: one day of one group.
        n.HasIndex(x => new { x.Status, x.SendAfter });
        n.HasIndex(x => new { x.Date, x.ClassId });
    }
}
