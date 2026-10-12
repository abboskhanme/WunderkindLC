-- CRM upgrade — Phase 2 (Telegram messaging core). Data SQL for the integrator's `CrmMessaging` migration
-- (docs/SPEC-crm-upgrade.md §0.3, P2.2). Paste into `migrationBuilder.Sql(...)` AFTER the schema operations.
--
-- Schema produced by the model (for the reviewer of the generated migration — NOTHING here is dropped):
--   TelegramRegistrations  + "Relation" varchar(16) NOT NULL DEFAULT '' , + "BlockedAt" timestamp NULL,
--                          + index ("StudentId", "BlockedAt")
--   Broadcasts             + "Kind" varchar(20) NOT NULL DEFAULT 'broadcast', + "AudienceJson" text NOT NULL DEFAULT '',
--                          + "AttachmentId" varchar(36) NULL, + "AckRequired" boolean NOT NULL DEFAULT false,
--                          + "Status" varchar(16) NOT NULL DEFAULT 'done', + "FinishedAt" timestamp NULL
--   AutoMessageRules       + "AckButton" boolean NOT NULL DEFAULT false, + "AttachmentId" varchar(36) NULL
--   MessageDeliveries      (new) — unique index "DedupeKey"; indexes ("Status","NextAttemptAt"), ("BroadcastId"),
--                          ("StudentId","CreatedAt"), ("SourceId")
--   MessageAttachments     (new)
--   StudentAchievements    (new) — index ("StudentId"); "CongratulationCount" int NOT NULL DEFAULT 0
-- ⚠️ `migrations add` may also emit DropTable/DropColumn for the Phase 1 legacy entities — delete those by hand.
-- ⚠️ Non-nullable new string columns of existing tables need a DEFAULT in the migration (EF emits defaultValue: ""
--    for strings without HasDefaultValue — check "AudienceJson").

-- Existing campaigns are finished broadcasts.
UPDATE "Broadcasts" SET "Kind" = 'broadcast', "Status" = 'done' WHERE "Kind" = '' OR "Kind" IS NULL;

-- Existing debt rules: the odd-day cadence is replaced by a monthly day (P2.5) — day 1 at 09:00.
UPDATE "AutoMessageRules" SET "ScheduleType" = 'monthly', "ScheduleDayOfMonth" = 1, "ScheduleTime" = '09:00'
 WHERE "Trigger" = 'payment_debt';

-- Birthday greetings keep their 09:00 time (now configurable per rule).
UPDATE "AutoMessageRules" SET "ScheduleTime" = '09:00' WHERE "Trigger" = 'birthday';

-- NOT here (C# logic, run once at startup by CrmMessagingSetup.RunStartupAsync, SeedMarker-guarded):
--   tg-relation-backfill-2026-10   — TelegramRegistrations.Relation = TelegramRelations.Detect(student, phone)
--   autorule-telegram-only-2026-10 — rules with no external channel (push-only) disabled; SendTelegram = true on all
