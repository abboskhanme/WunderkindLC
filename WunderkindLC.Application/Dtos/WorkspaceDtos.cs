namespace WunderkindLC.Application.Dtos;

/* =====================================================================================
   CRM upgrade — Phase 5 (docs/SPEC-crm-upgrade.md §6): dated schedule, conflicts, home layout
   ===================================================================================== */

/// <summary>Room column of the dated schedule. <paramref name="Id"/> — Room.Id, or <c>name:…</c> for a legacy
/// free-text room (same key as the weekday grid, <c>ScheduleGridExport.RoomKey</c>).</summary>
public record DatedRoomDto(string Id, string Name, int? Floor, int Capacity);

/// <summary>Teacher of the dated schedule (non-archived teachers + anyone teaching a listed lesson).</summary>
public record DatedTeacherDto(string Id, string Name);

/// <summary>
/// ONE REAL lesson on a date (<c>AttendanceWatch.LessonsOn</c>): moved lessons are on their NEW date/time
/// (<paramref name="Moved"/>), substitutes are named, the group lifetime is respected.
/// <paramref name="Members"/> — seat holders (<c>MembershipLifecycle.SeatHolders</c>).
/// <paramref name="AttendanceTaken"/> — the lesson is marked «o'tildi» in the journal (same as the lesson monitor).
/// <paramref name="Status"/> — <c>active</c> | <c>full</c> | <c>blocked</c> (group status filter).
/// </summary>
public record DatedLessonDto(
    string Date, string GroupId, string GroupName, string CourseId, string CourseName,
    string TeacherId, string TeacherName, string SubstituteTeacherId, string SubstituteTeacherName,
    string RoomId, string RoomName, int? Floor, string Start, string End, bool Moved,
    int Members, int Capacity, bool AttendanceTaken, string Status);

/// <summary><c>GET api/admin/schedule/dated?from&amp;to</c> (≤ 7 days). <paramref name="SkippedGroups"/> — groups
/// with no weekdays or a broken time (not on the board, but counted — never silently lost).</summary>
public record DatedScheduleDto(
    string From, string To, List<DatedRoomDto> Rooms, List<DatedTeacherDto> Teachers,
    List<DatedLessonDto> Lessons, int SkippedGroups);

/// <summary>Group reference inside a conflict pair.</summary>
public record ScheduleConflictGroupDto(string Id, string Name);

/// <summary><c>GET api/admin/schedule/conflicts</c> row — two existing groups booked into the same room or with
/// the same teacher at the same time. <paramref name="Days"/> — shared weekdays (0=Monday);
/// <paramref name="Slot"/> — the overlapping interval «15:00–15:30».</summary>
public record ScheduleConflictPairDto(
    string Kind, ScheduleConflictGroupDto GroupA, ScheduleConflictGroupDto GroupB, List<int> Days, string Slot);

/// <summary>One card/block entry of the home layout.</summary>
public record HomeLayoutItemDto(string Key, bool Visible);

/// <summary>Admin home page layout (per user, <c>UserSettings.HomeLayout</c>; rules: <c>HomeLayoutRules</c>).</summary>
public record HomeLayoutDto(int Version, bool CardsVisible, List<HomeLayoutItemDto> Cards, List<HomeLayoutItemDto> Blocks);

/// <summary><c>GET api/admin/home/layout</c> — <c>Layout = null</c>: the user never customised the page.</summary>
public record HomeLayoutResponseDto(HomeLayoutDto? Layout);

/// <summary><c>PUT api/admin/work-tasks/{id}/return</c> — why the task goes back to work (1..500 chars).</summary>
public record WorkTaskReturnInput(string? Reason);
