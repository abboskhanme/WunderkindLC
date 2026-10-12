-- =====================================================================================================
-- Phase 4 (CRM upgrade, migration `CrmTestsRating`) — data SQL for the integrator.
-- docs/SPEC-crm-upgrade.md §0.3 / §5 P4.1. Paste into the generated migration's Up() via migrationBuilder.Sql(...)
-- (or set the AddColumn defaultValue as noted). No DROP of any table/column belongs here.
-- =====================================================================================================

-- ---- U4b (rating engine) — rating settings on CenterMeta -------------------------------------------
-- The migration adds three NON-NULL columns to "CenterMeta":
--   "RatingTestWeight" integer, "RatingTopBottomCount" integer, "RatingTopBottomScope" varchar(8).
-- EF emits defaultValue 0 / '' for them. The model has NO HasDefaultValue on purpose (0 is a valid weight —
-- "ball only" — and a DB default would make EF skip an explicit 0 on INSERT). So the existing settings row must be
-- given the owner defaults (60 / 5 / 'group') RIGHT AFTER the AddColumn calls, in the same migration.
-- Either edit the three AddColumn calls to defaultValue: 60 / 5 / "group", or run:
UPDATE "CenterMeta"
SET "RatingTestWeight" = 60,
    "RatingTopBottomCount" = 5,
    "RatingTopBottomScope" = 'group';
-- ⚠️ Only inside the migration that ADDS the columns: run later it would overwrite an admin's saved weight.

-- ---- U4a (test entry, later unit) appends its own lines below ---------------------------------------
-- (SPEC P4.1: UPDATE "TestResults" SET "Kind" = 'interim' WHERE "Kind" = '' OR "Kind" IS NULL;)
