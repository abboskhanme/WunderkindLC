-- =====================================================================================================
-- Phase 4 — U4a (test result entry) + U4d (monthly TOP/BOTTOM): data SQL for the integrator.
-- docs/SPEC-crm-upgrade.md §0.3 / §5 P4.1. Paste into the Up() of the migration generated after this branch is
-- merged (e.g. `CrmTestsEntry`), via migrationBuilder.Sql(...), RIGHT AFTER the AddColumn calls below.
-- No DROP of any table/column belongs here. U4d adds NO schema (it reads CenterMeta.RatingTopBottom* from U4b and
-- stores its monthly markers in the existing "BackgroundJobRuns" table).
-- =====================================================================================================

-- ---- Schema the generated migration must contain (model: Domain/Crm/Rating.cs, AppDbContext.CrmRating.cs) ----
--   "TestResults"."Kind"            varchar(16)  NOT NULL  (EF emits defaultValue '')
--   "TestResults"."ResultsSentAt"   timestamp without time zone  NULL
--   "TestResults"."ResultsSentById" varchar(64)  NULL
-- Nothing else: "QuestionCount" already exists (legacy online-test column, reused as «Savollar soni»).

-- ---- Backfill: every existing test becomes «Oraliq» (interim) — SPEC P4.1 ----------------------------------------
UPDATE "TestResults" SET "Kind" = 'interim' WHERE "Kind" = '' OR "Kind" IS NULL;

-- Old tests keep their "MaxScore" as the denominator ("QuestionCount" = 0 for offline tests — the grid shows «Ball»,
-- range 0..MaxScore). Old ONLINE tests already have MaxScore = QuestionCount. Nothing else to backfill:
-- "ResultsSentAt"/"ResultsSentById" stay NULL (never sent).
