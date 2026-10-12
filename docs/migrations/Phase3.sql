-- CRM upgrade Phase 3 «Attendance» — data SQL for the integrator's `CrmAttendance` migration
-- (docs/SPEC-crm-upgrade.md §0.3, §4). Paste into `Up()` via migrationBuilder.Sql(...) AFTER the
-- AddColumn/CreateTable operations of the phase. All statements are idempotent.
--
-- New columns (for the reviewer of the generated migration — none of them may be DROPPED):
--   LessonNotes:      AttendanceTakenAt (timestamp NULL), AttendanceTakenById (varchar(200) NULL)
--                     + index (ClassId, Date)
--   AbsenceReasons:   IsExcused (boolean NOT NULL default false)
--   CenterMeta:       AttendanceDisciplineEnabled (bool), AttendanceDeadlineMinutes (int),
--                     AttendanceReminderMinutes (varchar(32)), AttendanceEscalateEnabled (bool),
--                     AttendanceFineEnabled (bool), AttendanceFineAmount (numeric(18,2)),
--                     AbsentNoticeDelayMinutes (int), AbsentNoticeApproval (varchar(16)),
--                     AbsenceStreakReasonId (varchar(200) NULL)
-- New tables:
--   TeacherAttendanceFines  (unique (GroupId, LessonDate); index (TeacherId, Month); index LessonDate)
--   AbsenceStreakAlerts     (unique (StudentId, GroupId, StreakStart, Level))

-- 1) U3a — excused / unexcused backfill (owner decision 2026-10-11): every reason that is not «late» and
--    whose name does not contain "sababsiz" becomes EXCUSED. Editable afterwards in Sozlamalar → Sabablar.
UPDATE "AbsenceReasons" SET "IsExcused" = TRUE
 WHERE "IsLate" = FALSE AND lower("Name") NOT LIKE '%sababsiz%';

-- 2) U3a — CenterMeta defaults for the EXISTING row (EF adds the new columns with 0 / false / '').
--    New rows get the same values from the C# initialisers (CenterMeta in Domain/Crm/Attendance.cs).
UPDATE "CenterMeta" SET
    "AttendanceDeadlineMinutes" = 15,
    "AttendanceReminderMinutes" = '5,10',
    "AttendanceEscalateEnabled" = TRUE,
    "AbsentNoticeDelayMinutes"  = 1,
    "AbsentNoticeApproval"      = 'late_only'
 WHERE "AttendanceDeadlineMinutes" = 0;

-- 3) U3a — AttendanceTakenAt backfill for the last 60 days: the monitor («Davomat qilinmagan guruhlar»,
--    any past date) and the fine job's "yesterday" scan now read this column. A conducted lesson that has
--    a bulk attendance (AttendanceTaken) or at least one «keldi»/reason mark gets the lesson DATE at 00:00
--    ("time unknown" — always ON TIME, so deploying never creates fines for history).
--    Older rows stay NULL (only the monitor's history view would read them).
--    `AttendanceTaken` (= complete) keeps its existing meaning for old rows (bulk attendance set it).
UPDATE "LessonNotes" n
   SET "AttendanceTakenAt" = CAST(n."Date" AS date)::timestamp
 WHERE n."AttendanceTakenAt" IS NULL
   AND n."Conducted" = TRUE
   AND n."Date" >= to_char(current_date - 60, 'YYYY-MM-DD')
   AND (n."AttendanceTaken" = TRUE
        OR EXISTS (SELECT 1 FROM "JournalEntries" e
                    WHERE e."ClassId" = n."ClassId" AND e."SubjectId" = n."SubjectId"
                      AND e."Date" = n."Date" AND e."Period" = n."Period"
                      AND (e."Present" = TRUE OR e."ReasonId" IS NOT NULL)));
