-- CRM upgrade — pre-release review fixes (2026-10-12) — data SQL for the integrator's migration
-- (docs/SPEC-crm-upgrade.md §0.3). Paste into `Up()` via migrationBuilder.Sql(...) AFTER the AddColumn operations.
-- All statements are idempotent.
--
-- New columns (for the reviewer of the generated migration — none of them may be DROPPED):
--   CenterMeta:  AttendanceDisciplineSince (timestamp without time zone NULL),
--                AttendanceFineSince       (timestamp without time zone NULL)
-- No new tables. No schema change for the other fixes (bot history scrub runs once at startup — SeedMarker
-- `bot-history-credentials-scrub-2026-10`, CrmMessagingSetup).

-- 1) «Since» moments of switches that are ALREADY on (dev/test databases where Phase 3 ran before this fix):
--    stamped with the deploy moment in centre time (UTC+5, AppClock) so no lesson before the deploy becomes
--    retroactively due for a reminder, escalation or fine. Rows with the switch off stay NULL.
UPDATE "CenterMeta"
   SET "AttendanceDisciplineSince" = (now() AT TIME ZONE 'Asia/Tashkent')
 WHERE "AttendanceDisciplineEnabled" = TRUE AND "AttendanceDisciplineSince" IS NULL;

UPDATE "CenterMeta"
   SET "AttendanceFineSince" = (now() AT TIME ZONE 'Asia/Tashkent')
 WHERE "AttendanceFineEnabled" = TRUE AND "AttendanceFineSince" IS NULL;
