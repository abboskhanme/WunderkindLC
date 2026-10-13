using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Abstractions;

/// <summary>CRM upgrade Phase 3 «Attendance» tables (docs/SPEC-crm-upgrade.md §4).</summary>
public partial interface IAppDbContext
{
    DbSet<TeacherAttendanceFine> TeacherAttendanceFines { get; }
    DbSet<AbsenceStreakAlert> AbsenceStreakAlerts { get; }
    DbSet<TeacherFine> TeacherFines { get; }
}
