-- CRM upgrade Phase 3, U3b — absent-notice queue with manager hold (docs/SPEC-crm-upgrade.md P3.3).
-- Integrator: generate `CrmAbsentNotices` (dotnet ef migrations add CrmAbsentNotices ...) after merge, read it
-- (no DROPs expected: only AddColumn ×9 + 2 indexes on "AttendanceNotifyItems"), then paste the statement below
-- into its Up() AFTER the AddColumn calls (migrationBuilder.Sql(...)).
--
-- New columns on "AttendanceNotifyItems":
--   "SendAfter" timestamp NOT NULL, "LessonStart" varchar(5) NOT NULL DEFAULT '', "LessonEnd" varchar(5) NOT NULL DEFAULT '',
--   "TakenLate" boolean NOT NULL DEFAULT false, "DecidedById" varchar(200) NULL, "DecidedByName" varchar(200) NULL,
--   "DecidedAt" timestamp NULL, "DecisionNote" varchar(300) NULL, "CancelReason" varchar(32) NULL
-- New indexes: ("Status", "SendAfter"), ("Date", "ClassId").

-- Existing rows keep their original schedule: send time = creation time (old rows were sent immediately).
-- Rows still `pending` at deploy are therefore due at once and the worker expires those older than 6 h
-- (same as before: a stale «kelmadi» never goes the next day).
UPDATE "AttendanceNotifyItems" SET "SendAfter" = "CreatedAt";
