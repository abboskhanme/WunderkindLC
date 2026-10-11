# CRM upgrade — Phases 2–6 SPEC (2026-10-11)

Single source of truth for the build agents of the CRM upgrade (client meeting script, 2026-10-11).
Inputs: the client script (Uzbek, 5 sections), `docs/GAP-crm-takomillashtirish.md` (what exists today),
the owner's binding decisions (`docs/ASSUMPTIONS.md`, entries `[2026-10-11]`), `.claude/rules/*.md`.
Every decision this SPEC makes on its own is logged as a one-liner in `docs/ASSUMPTIONS.md` (`[2026-10-11]`).

Language: prose and identifiers English; every UI string quoted here is the exact Uzbek (Latin) text to ship.

---

## 0. Ground rules for every phase

### 0.1 State after Phase 1 (removal) — what this SPEC assumes

Phase 1 is built by another agent and MUST be merged before any unit below starts. It leaves:

- no SMS (Eskiz, Local SMS), no push sending (`FcmService.SendAsync`), no group chat, no Local Call/CTI UI,
  no APK sending, no ONLINE mode of group tests (bot PDF, answer key, test code, external participants);
- **tables and columns kept** (code-only removal): `SmsLogs`, `SmsTemplates`, `ChatMessages`, `DeviceTokens`,
  `AutoMessageRule.SendSms/SendPush/SmsProvider`, `TestResult.Mode/PdfUrl/AnswerKey/Code/...`, `ExternalTestScores`;
- `NotificationStore` (web cabinet bells) decoupled from the old `SendPush` flag;
- Telegram is the only outbound channel.

Verify before starting (grep on `main`): no non-test reference to `EskizService`, `CtiSmsService`, `ChatService`,
`OnlineTestBotService`. If Phase 1 removed the C# class `SmsTemplate` but kept the table, Phase 2 maps a new
entity `MessageTemplate { Id, Name, Text, Order }` to the SAME table (`ToTable("SmsTemplates")`); otherwise it
reuses `SmsTemplate` as is. Either way no data is lost.

⚠️ If Phase 1 removed entity classes from the EF model while keeping their tables, the next
`migrations add` will emit `DropTable`/`DropColumn` for them. **Delete those operations by hand** — no phase
in this SPEC drops a table or a column (dropping is a separate, owner-confirmed step).

### 0.2 Phase 0 — conflict-proofing prep (one small sequential PR, before any parallel work)

Without this, five agents would edit the same 5 800-line `Entities.cs`, `AppDbContext.cs`, `IAppDbContext.cs`
and `Program.cs` at the same anchors.

1. Mark `partial` (no other change): `AppUser`, `Student`, `Room`, `TestResult`, `LessonNote`, `AbsenceReason`,
   `CenterMeta`, `AutoMessageRule`, `Broadcast`, `TelegramRegistration`, `UserSettings`, `WorkTask`
   (all in `WunderkindLC.Domain/Entities.cs`) and `AttendanceNotifyItem` (`Entities.AttendanceNotify.cs`).
   Phases add properties in their own files under `WunderkindLC.Domain/Crm/` (`Messaging.cs`,
   `Attendance.cs`, `Rating.cs`, `Workspace.cs`, `Accounts.cs`).
2. `public partial class AppDbContext` and `public partial interface IAppDbContext`. In `AppDbContext.cs`
   declare and call (as the LAST statements of `OnModelCreating`):
   ```csharp
   partial void ConfigureCrmMessaging(ModelBuilder b);   // Phase 2
   partial void ConfigureCrmAttendance(ModelBuilder b);  // Phase 3
   partial void ConfigureCrmRating(ModelBuilder b);      // Phase 4
   partial void ConfigureCrmWorkspace(ModelBuilder b);   // Phase 5
   partial void ConfigureCrmAccounts(ModelBuilder b);    // Phase 6
   ```
   Each phase implements its method + DbSets in `Infrastructure/Data/AppDbContext.Crm<Name>.cs` and adds its
   DbSets to `Application/Abstractions/IAppDbContext.Crm<Name>.cs`.
3. `public partial class` for `StudentsController`, `StudentPortalController`, `ClassesController`,
   `TestResultsController` (TeacherPortalController and WorkTasksController already are). New endpoints go
   into new partial files (`StudentsController.<Feature>.cs`) or new controllers. A partial file that needs a
   service not in the primary constructor resolves it with `HttpContext.RequestServices.GetRequiredService<T>()`
   (existing pattern in `TeacherPortalController`) — do NOT edit the shared primary constructor.
4. `WunderkindLC.Server/Crm/CrmSetup.cs` with five empty extension methods
   `AddCrmMessaging(this IServiceCollection)` … `AddCrmAccounts(...)` and five empty
   `Task RunCrm<Name>StartupAsync(IServiceProvider)` hooks; `Program.cs` calls all ten once (DI section and
   right after `Migrate()` + existing seeds). Each phase fills only its own method, in its own file
   `Server/Crm/Crm<Name>Setup.cs` (move the stub there).
5. EF interceptors become pluggable: keep `AddSingleton<CacheInvalidationInterceptor>()`, add
   `AddSingleton<IInterceptor>(sp => sp.GetRequiredService<CacheInvalidationInterceptor>())`, and change the
   `AddDbContext` line to `.AddInterceptors(sp.GetServices<IInterceptor>())`. Phase 6 then registers its
   interceptor as another `IInterceptor` from `AddCrmAccounts` without touching `Program.cs`.

### 0.3 Migration policy

- Build branches do **not** commit migrations (tests use `EnsureCreated`, `WunderkindLC.Tests/TestDb.cs`).
- The integrator generates exactly one migration per phase AFTER the phase is merged into `main`:
  `CrmMessaging`, `CrmAttendance`, `CrmTestsRating`, `CrmWorkspace`, `CrmAccounts`
  (`dotnet ef migrations add <Name> --project WunderkindLC.Infrastructure --startup-project WunderkindLC.Server --no-build`),
  reads it (global rule: remove spurious DROPs), adds the data SQL listed in the phase's "Migration" block,
  and verifies `dotnet ef database update` on a fresh database AND on a restored prod-schema dump.
- Data fixes that need C# logic (phone normalisation) run once at startup inside the phase's
  `RunCrm<Name>StartupAsync`, guarded by a `SeedMarker` key (`FinanceTypeSeed` naqshi) and idempotent.

### 0.4 Cross-cutting conventions (apply in every phase)

| Topic | Rule |
|---|---|
| Time | Server "now" = `AppClock.Now` (UTC+5, local, `DateTime` kind Unspecified); dates `yyyy-MM-dd`, times `HH:mm`. Never `DateTime.Now`. Telegram/NVR style UTC conversion is not needed here. |
| Background jobs | Daily/once markers in the DB: `JobRunMarker` (per-day keys) or `LessonReminderMark` (per-lesson keys) or a unique index. Never an in-memory "already done" flag. Queues follow `AttendanceNotifyWorker`: claim by concurrency token (`pending → sending`), on startup every `sending` row becomes `unknown` and is NOT resent, expiry, pruning. |
| Pure rules | Every decision rule is a static pure function in `Application/Services/...` with xUnit tests (reference: `ContactService`, `AttendanceWatch`, `TelegramLinks`). Controllers only orchestrate. |
| Errors | Expected failures → `BadRequest/Conflict/StatusCode(403, new { message = "<Uzbek reason>" })`. Service `InvalidOperationException` caught in the controller. Client shows every write error with `apiErrorMessage(err, '<fallback>')`; modals stay open, input is kept (`.claude/rules/error-visibility.md`). |
| Inputs | Numeric fields use `NumberInput`/`MoneyInput` — never `type="number"`; integers only (`.claude/rules/number-inputs.md`). |
| RBAC | Every new endpoint has `[AdminPerm(...)]` (admin area), `[Authorize(Roles = ...)]` (portals/own data) or an explicit hard-role check. New page keys go through the 4 steps of `.claude/rules/permissions.md` §3.1; `PermissionCatalogTests` must stay green. Sensitive GETs use `ReadRequiresPerm = true`. |
| Audit | `audit.Record(...)` before `SaveChanges`; each new `EntityType` mapped in `AuditSections.ByEntityType` (`AuditSectionsTests`). Settings changes via `SettingsAudit.Record<CenterMeta>(..., withValues: true)` with Uzbek labels. Never a password, token, chat id or `/uploads/...` path in an audit row. |
| Backup | Every new table is either added to `BackupService.CollectAsync` or listed in `ExcludedTables` (rule `admin-core.md` §4). |
| MCP | MCP tools are a separate follow-up unit per phase (§8). New tables are closed to the read-only role until a tool lists them in `Reads`; columns named `…ChatId`, `…Password…`, `…Url`, `…FileId` are never granted (`McpReadOnlyGrants.IsSensitiveColumn`). |
| Telegram | Parent/student-facing messages go ONLY through the Phase 2 outbox (`TelegramOutbox`). Staff/teacher operational messages (reminders, escalations, task notices) may keep direct `TelegramService` calls. `parse_mode` HTML only through `TelegramHtml` (escaping). |
| Caching | New heavy reads use `DataCache.GetOrCreateAsync(key, deps (entity type names), ttl, factory)`; deps must include every entity the factory reads. |

### 0.5 Reference implementations (copy their shape)

| Need | Copy from |
|---|---|
| DB queue + worker | `AttendanceNotifyItem` + `AttendanceNotifyWorker`; `SmsQueueJob/Item` (resume/unknown/expiry) |
| Pure rule + tests | `ContactService` + `ContactServiceTests`; `AttendanceWatch` + `AttendanceWatchTests` |
| Daily scheduled sender | `CustomReminderService` (`IsScheduledDay`, `JobRunMarker` per recipient) |
| Encrypted column | `FaceVault` (AES-256-GCM, `.env` key, fail-closed) |
| Settings modal + audit | `JournalPolicyModal` + `SettingsController` journal policy + `SettingsAudit` |
| List page with columns | `StudentListBoard` (`ActiveStudentsPage`) |
| One-time data fix | `FinanceTypeSeed` (`SeedMarker`) |

---

## 1. Goal

The centre runs on web + Telegram only. Parents must get correct, well-formatted, trackable Telegram messages
(with media and an "I've got it" button) about attendance, debts, tests, achievements and promotions — while
managers can stop a wrong "absent" notice and teachers are pushed (reminders, fines) to take attendance during
the lesson. Test results become the main input of a clean student rating visible in the admin profile and the
parent cabinet; admins get configurable home widgets, a date-based schedule with floors and no double
bookings, task approval, and permanently visible student logins/passwords.

---

## 2. Phase map

| Phase | Name | Units (built by) | Depends on |
|---|---|---|---|
| 0 | Prep | U0 | Phase 1 merged |
| 2 | Telegram messaging core | U2a core · U2b auto messages · U2c achievements · U2d client · U2m MCP | U0 |
| 3 | Attendance | U3a one definition + excused flag · U3b absent-notice queue · U3c discipline (reminders/fines) · U3d streaks/red list/profile · U3m MCP | U3a: U0 · U3b: U3a + U2a · U3c, U3d: U3a |
| 4 | Tests & rating | U4a test entry + send results · U4b rating engine · U4c rating UI · U4d TOP/BOTTOM · U4m MCP | U4b: U0 · U4c: U4b · U4a: U2b · U4d: U4b + U2b |
| 5 | Dashboard, schedule, tasks | U5a schedule · U5b tasks · U5c home widgets · U5m MCP | U5a, U5b: U0 · U5c: U3b + U3d + U5a |
| 6 | Accounts | U6a credential vault · U6b Telegram column · U6m MCP | U6a: U0 · U6b: U2a |

Parallel plan, shared-file list and merge order: §8.

---

## 3. Phase 2 — Telegram messaging core

### P2.1 Scope

Templates with tokens + server-side preview; Telegram HTML formatting (bold/italic/underline/link) with
escaped token values; one attachment (photo/video/document) per message; recipient count before sending;
per-recipient delivery log (outbox); «Qabul qildim» button + acknowledgement status in the CRM; active vs
archive audiences strictly separate; "all parents" = connected, active, not blocked; 2–3 Telegram accounts per
student with a relation; message types: absent notice (transport only — timing is Phase 3), debt reminder
(filtered manual send + automatic on a configurable day), "01" thank-you on bot connect, birthday, test
results (catalog only — dispatch is U4a), monthly TOP/BOTTOM (catalog only — U4d), achievements, archive
broadcast.

### P2.2 Data model (U2a unless noted)

All new entities in `WunderkindLC.Domain/Crm/Messaging.cs`; config in `ConfigureCrmMessaging`.
Indexed string columns get `HasMaxLength(200)` (project convention).

**`TelegramRegistration` (partial, extend)**

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `Relation` | string(16) | no | `""` | `father` · `mother` · `parent` · `student` · `other`; `""` on teacher/staff rows |
| `BlockedAt` | DateTime | yes | null | set when Telegram says the user blocked the bot / chat is gone; null = deliverable |

Index `(StudentId, BlockedAt)`. Existing unique `(StudentId, ChatId)` and `(ChatId)` stay.

**`MessageDelivery`** (new, table `MessageDeliveries`) — the outbox AND the per-recipient log.

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `Id` | string(36) | no | Guid | PK |
| `BroadcastId` | string(36) | yes | | campaign (`Broadcast.Id`); null for auto messages |
| `Source` | string(16) | no | | `campaign` · `auto` |
| `Trigger` | string(40) | no | | campaign kind (`broadcast`, `archive`, `debt`) or `AutoMessageTriggers` key |
| `SourceId` | string(36) | yes | | originating record: attendance notice item, test, achievement, rule id … (index) |
| `StudentId` | string(36) | yes | | subject student (index with `CreatedAt`) |
| `TeacherId` | string(36) | yes | | teacher recipient (staff messages) |
| `RegistrationId` | string(36) | yes | | `TelegramRegistration.Id` |
| `ChatId` | long | no | | destination (never returned by any API) |
| `Relation` | string(16) | no | `""` | snapshot |
| `RecipientName` | string(200) | no | `""` | snapshot: student full name (teacher name for staff rows) |
| `Text` | text | no | `""` | final Telegram-HTML (≤ 4096 visible chars; ≤ 1024 when used as caption) |
| `AttachmentId` | string(36) | yes | | `MessageAttachment.Id` |
| `AckRequired` | bool | no | false | adds the «✅ Qabul qildim» button |
| `Status` | string(16) | no | `pending` | **concurrency token**: `pending` · `sending` · `sent` · `failed` · `blocked` · `unknown` · `expired` · `cancelled` |
| `Attempts` | int | no | 0 | |
| `NextAttemptAt` | DateTime | no | =CreatedAt | worker picks `pending` rows with `NextAttemptAt <= now` |
| `ExpiresAt` | DateTime | no | | not sent after this → `expired` |
| `DedupeKey` | string(200) | no | =Id | **unique index**; natural key when the event must not repeat; keys longer than 200 chars are stored as `"h:" + sha256hex(key)` |
| `TelegramMessageId` | long | yes | | id of the message that carries the button |
| `Error` | string(300) | no | `""` | Telegram description (sanitised; never the bot token) |
| `CreatedAt` | DateTime | no | AppClock.Now | |
| `SentAt` | DateTime | yes | | |
| `AckAt` | DateTime | yes | | first press of the button |
| `CreatedById` | string(36) | yes | | sender for campaigns |

Indexes: `(Status, NextAttemptAt)`, `(BroadcastId)`, `(StudentId, CreatedAt)`, `(SourceId)`, unique `(DedupeKey)`.
Retention: rows with `CreatedAt` older than **180 days** are deleted daily by the worker (batches of 5 000).
Backup: `ExcludedTables` += `"messageDeliveries"` (log-like, large).

**`MessageAttachment`** (new, table `MessageAttachments`)

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | string(36) | no | PK |
| `Kind` | string(16) | no | `photo` · `video` · `document` |
| `FileName` | string(200) | no | original name (sanitised: path separators removed, ≤ 200) — shown in UI and used as Telegram filename for documents |
| `FileUrl` | string(300) | no | internal storage path `/uploads/messaging/{guid:N}{ext}` (never returned by an API) |
| `ContentType` | string(100) | no | |
| `SizeBytes` | long | no | |
| `TelegramFileId` | string(300) | no | `""` until first successful upload; reused afterwards |
| `CreatedAt`, `CreatedById`, `CreatedByName` | | | |
| `FileDeletedAt` | DateTime? | yes | local file removed by cleanup (Telegram `file_id` keeps working) |

Backup: included (`"messageAttachments"`, metadata only).

**`Broadcast`** (partial, extend) — becomes the campaign header.

| Column | Type | Default | Notes |
|---|---|---|---|
| `Kind` | string(20) | `"broadcast"` | `broadcast` · `archive` · `debt` |
| `AudienceJson` | text | `""` | the `AudienceSpec` snapshot |
| `AttachmentId` | string(36)? | null | |
| `AckRequired` | bool | false | |
| `Status` | string(16) | `"done"` | `queued` · `sending` · `done` · `cancelled` (existing rows = `done`) |
| `FinishedAt` | DateTime? | null | |

`ClassName` keeps holding the human audience label; `RecipientCount` = planned messages. Counts shown in the UI
are computed from `MessageDeliveries`; legacy rows (no deliveries) show `RecipientCount/SentCount`.

**`AutoMessageRule`** (partial, extend — U2b)

| Column | Type | Default | Notes |
|---|---|---|---|
| `AckButton` | bool | false | |
| `AttachmentId` | string(36)? | null | optional media sent with every message of the rule (e.g. birthday card) |

Existing `ScheduleType/ScheduleTime/ScheduleDayOfMonth` are reused by scheduled triggers (P2.5).

**`StudentAchievement`** (new, table `StudentAchievements`, U2c)

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | string(36) | no | PK |
| `StudentId` | string(36) | no | index |
| `Kind` | string(16) | no | `ielts` · `cefr` · `certificate` · `olympiad` · `other` |
| `Title` | string(200) | no | e.g. "IELTS", "Cambridge KET sertifikati" |
| `Result` | string(50) | no | e.g. "7.5", "B2", "" |
| `Date` | string(10) | no | `yyyy-MM-dd`, ≤ today |
| `Note` | string(1000) | no | |
| `AttachmentId` | string(36)? | yes | photo shown in profile/cabinet and sent with the congratulation |
| `CongratulatedAt` | DateTime? | yes | last time the message was queued |
| `CreatedAt`, `CreatedById`, `CreatedByName` | | | |

Backup: included. `StudentPurge` deletes the student's achievements (add to its preview list: «Yutuqlar»).

**Migration `CrmMessaging` data SQL**
```sql
UPDATE "Broadcasts" SET "Kind" = 'broadcast', "Status" = 'done' WHERE "Kind" = '' OR "Kind" IS NULL;
-- existing debt rules: odd-day cadence is replaced by a monthly day (P2.5)
UPDATE "AutoMessageRules" SET "ScheduleType" = 'monthly', "ScheduleDayOfMonth" = 1, "ScheduleTime" = '09:00'
 WHERE "Trigger" = 'payment_debt';
UPDATE "AutoMessageRules" SET "ScheduleTime" = '09:00' WHERE "Trigger" = 'birthday';
```
Startup data fixes (`RunCrmMessagingStartupAsync`, SeedMarker-guarded):
1. `tg-relation-backfill-2026-10`: for every registration with `StudentId != ""` and `Relation == ""` set
   `Relation = TelegramRelations.Detect(student, reg.Phone)`.
2. `autorule-telegram-only-2026-10`: every rule with `Enabled && !SendTelegram` → `Enabled = false`
   (log the rule names — it was an SMS/push-only rule); then `SendTelegram = true` on all rules. From now on
   `Enabled` alone decides; every save sets `SendTelegram = true`.

### P2.3 Pure rules (Application/Services/Messaging/)

**`TelegramHtml`** (static, `TelegramHtmlTests`)
- `SanitizeTemplate(string text) → (string Html, IReadOnlyList<string> Errors)`. Allowed tags (case-insensitive)
  and normalisation: `b`, `strong`→`b`, `i`, `em`→`i`, `u`, `ins`→`u`, `s`, `strike`/`del`→`s`, `code`, `pre`,
  `a href="…"` (only `http:`, `https:`, `tg:` schemes; any other attribute dropped). Any other `<…>`, stray `<`,
  `>` is escaped as text. `&` is escaped unless it starts `&lt;`, `&gt;`, `&amp;`, `&quot;`, `&#NNN;`, `&#xHH;`.
  Errors (Uzbek): unclosed/mis-nested allowed tag → `"Formatlash yopilmagan: <b>"`; bad link →
  `"Havola noto'g'ri: faqat http(s) manzil"`.
- `Escape(string? value)` — HTML-escape a token value (`&`, `<`, `>`, `"`); `null` → `""`.
- `ToPlain(string html)` — strip tags, decode entities (used for the in-app bell, previews' plain view, fallbacks).
- `VisibleLength(string html)` = `ToPlain(html).Length`.
- Limits: `MaxText = 4096`, `MaxCaption = 1024`.

**`TelegramRelations`** (static)
- `Detect(Student s, string? phone) → string`: compare `PhoneUtil.Key(phone)` (≥ 7 digits) in this order:
  `FatherPhone` → `father`, `MotherPhone` → `mother`, `ParentPhone` → `parent`, `Phone` → `student`; no match →
  `other`. (Parents are more likely owners of a shared number.)
- `IsParent(relation)` = relation ∈ {father, mother, parent, other, ""} (`""` = not yet backfilled → parent).
- `Label(relation)`: father «Otasi», mother «Onasi», parent «Ota-onasi», student «O'zi», other «Boshqa».

**`MessageRender`** (static)
- `RenderStudent(template, student, reg?, group?, teacherName?, extra?) → (string Html, IReadOnlyList<string> Errors)`:
  step 1 `TelegramHtml.SanitizeTemplate(template)` (admin markup validated, stray `<`/`&` escaped); step 2 token
  substitution through `MessageTokenizer` with every substituted value HTML-escaped (add `bool html = false` to
  `MessageTokenizer.Student/Teacher/ApplyExtra` and the private `Rep`; when `true`, values pass through
  `TelegramHtml.Escape`). Values can therefore never inject tags. Plain system texts (e.g. the debt text built by
  `DebtMessages`) are passed through `TelegramHtml.Escape` before they are enqueued.
- `RenderTeacher(template, teacher, group?, extra?)` — same, via `MessageTokenizer.Teacher` (teachers audience).
- `HasStudentTokens(template)` — true if any `MessageTokenCatalog` token of group `student` (or `{guruh}`,
  `{oqituvchi}`, `{dars_*}`, `{qarzdorlik}`) appears. False ⇒ the campaign sends ONE message per chat
  (rendered for the chat's first student by `FullName` ascending, then `Id`; the delivery row's `StudentId` is that
  student), otherwise one per (student, chat).
- `UnknownTokens(template)` — `\{[a-z_\-]+\}` occurrences not in `MessageTokenCatalog` (case-insensitive).
- Header for auto messages: `"<b>🔔 " + Escape(rule.Name or trigger label) + "</b>\n\n" + body`
  (debt: `"<b>💰 To'lov eslatmasi</b>\n\n"`; campaigns: `"<b>📢 Markaz e'loni</b>\n\n"` for kind
  broadcast/archive, no header for debt campaigns — the system debt text is self-explanatory).

**`AudienceSpec`** (record, JSON body of several endpoints)
```ts
type Relation = 'father' | 'mother' | 'parent' | 'student' | 'other'
interface AudienceSpec {
  kind: 'active' | 'archive' | 'selected' | 'teachers'
  groupIds?: string[]            // active
  courseIds?: string[]           // active, archive
  teacherId?: string             // active: group's current teacher
  memberStatuses?: ('active' | 'trial' | 'frozen')[]   // active; default ['active','trial']
  onlyDebtors?: boolean          // active: Student.Balance < 0
  studentIds?: string[]          // selected: 1..2000 ids
  relations?: Relation[]         // default ['father','mother','parent','other']
  archivedFrom?: string; archivedTo?: string            // archive: Student.ArchivedAt range (yyyy-MM-dd)
  archiveReason?: string         // archive: case-insensitive substring of ArchiveReason
  teacherIds?: string[]          // teachers: empty = every non-archived teacher with a chat
}
```
Resolution (`MessageAudienceResolver.ResolveAsync`, IO, one query per table — no N+1):
- `active`: `!IsArchived`; student has ≥ 1 live membership (`StudentGroup.IsActive` and group not archived)
  whose `Status ∈ memberStatuses` and that matches `groupIds`/`courseIds` (group's `CourseId`)/`teacherId`
  (group's `TeacherId`) when given. Legacy students with NO membership at all (ClassName model) are included
  only when no group/course/teacher filter is set. `MessagingAudience.ClosedGroupStudentIdsAsync` students are
  excluded (existing rule).
- `archive`: `IsArchived`; filters as above; `courseIds` matches any membership ever (`StudentGroup` of a group
  with that course).
- `selected`: exactly those ids (archived or not — the UI only offers selection inside one list).
- `teachers`: chats of `TelegramRegistration.TeacherId`.
- Registrations: relation ∈ `relations`; `BlockedAt != null` rows are excluded and counted as `blocked`.
- Result: targets `(Student|Teacher, Registration)`, counts (see `audience/count`).

**`AutoMessageSchedule`** (static)
- `ModeOf(trigger)`: `monthly_day_time` for `payment_debt`, `rating_top`, `rating_bottom`; `daily_time` for
  `birthday`; `custom` for `custom_schedule` (existing daily/monthly); `none` otherwise.
- `IsDue(rule, now)`: `monthly_day_time` → `CustomReminderService.IsScheduledDay(rule with ScheduleType "monthly", today)`
  (month-end clamp) and `now.TimeOfDay >= ScheduleTime`; `daily_time` → `now.TimeOfDay >= ScheduleTime`.

### P2.4 Outbox, worker, acknowledgement, blocking (U2a)

**`ITelegramSender`** (new interface in Application; `TelegramService` implements it; tests use a fake):
```csharp
Task<TgSendOutcome> SendAsync(TgSend msg, CancellationToken ct);
// TgSend: ChatId, HtmlText, Media (Kind, FileId or Stream+FileName+ContentType), ReplyMarkup, ParseHtml(bool)
// TgSendOutcome: Result (Ok|Blocked|RateLimited|BadMarkup|Failed), MessageId, MediaFileId, RetryAfterSeconds, Description
```
`TelegramService.ClassifySendError(int status, string? description)` (pure, `TelegramSendErrorTests`):
403 or description containing `bot was blocked by the user` / `user is deactivated` /
`bot can't initiate conversation` / `chat not found` → `Blocked`; 429 or `Too Many Requests` → `RateLimited`
(+ `parameters.retry_after`); 400 `can't parse entities` → `BadMarkup`; everything else (incl. HTTP 200 without
`ok:true`, network errors, timeouts) → `Failed`. Media send: `sendPhoto`/`sendVideo`/`sendDocument` multipart,
**streamed from disk** (`StreamContent`, never `byte[]` of the whole video — 1 GB RAM server), or by cached
`file_id`; caption + `parse_mode=HTML` + `reply_markup`. The `"telegram"` HttpClient's 10 s timeout is too
short for a 50 MB upload: media sends use a dedicated named client `"telegram-media"` (register in
`Program.cs` via `AddCrmMessaging`) with a 120 s timeout.

**`TelegramOutbox`** (IO, no `SaveChanges` — caller saves, then calls `MessageDeliveryWorker.Wake()`):
- `Enqueue(db, OutboxItem item)` → adds a `MessageDelivery`; if a row with the same `DedupeKey` is already
  tracked/stored it is skipped (check `db.MessageDeliveries.Local` + `AnyAsync`; the unique index is the last line
  of defence — on `DbUpdateException` for that key the caller logs and continues, `LeadNotifier` detach naqshi).
- `ExpiresAt`: campaigns `CreatedAt + 12 h`; auto messages `CreatedAt + 6 h`.

**`MessageDeliveryWorker`** (singleton `BackgroundService`, registered in `AddCrmMessaging`)
- Startup (and once a day): every `sending` → `unknown` (not resent, startup only); prune rows older than
  180 days; delete the LOCAL file of attachments that are older than 60 days, already have a `TelegramFileId`
  (uploaded at least once), and are referenced by no `pending/sending` row, no enabled rule's `AttachmentId` and
  no `StudentAchievement` (achievement photos are displayed in the profile/cabinet and are never deleted)
  (`FileDeletedAt = now`). Later sends reuse the `file_id`; an attachment without `file_id` is never deleted.
- Loop: wait `Wake()` or 5 s; take up to 200 `pending` rows with `NextAttemptAt <= now` ordered by
  `NextAttemptAt, CreatedAt`; for each: expired → `expired`; claim `pending→sending` (concurrency token;
  `DbUpdateConcurrencyException` → skip); send in its own DI scope; finalise:
  - `Ok` → `sent`, `SentAt`, `TelegramMessageId`; first upload stores `MediaFileId` into `MessageAttachment.TelegramFileId`.
  - `Blocked` → `blocked`; `TelegramBlocks.MarkBlockedAsync(chatId)` sets `BlockedAt = now` on ALL registrations
    of that chat.
  - `RateLimited` → back to `pending`, `NextAttemptAt = now + retry_after` (whole worker also pauses for
    `retry_after` when the description says the bot is flooded).
  - `BadMarkup` → resend once immediately with `ToPlain(text)` and no parse mode; result decides.
  - `Failed` → `Attempts++`; `< 3` → `pending`, `NextAttemptAt = now + (1, 5) min`; else `failed`.
- Throttle: ≤ 20 messages/second globally; ≥ 1.1 s between two messages to the same chat (rows for a chat
  that is "too soon" get `NextAttemptAt = last + 1.1 s`).
- Multi-part: text longer than `MaxCaption` with an attachment → send the media WITHOUT caption, then the text
  as a second message; the button (if `AckRequired`) is on the LAST message; `TelegramMessageId` = that message.
- When a campaign has no `pending/sending` rows left → `Broadcast.Status = done`, `FinishedAt = now`.

**Ack button / callback**
- Keyboard: `{ inline_keyboard: [[{ text: "✅ Qabul qildim", callback_data: "mack:" + deliveryId }]] }`
  (prefix `mack:` is not used by any existing handler — current prefixes: `check_sub*`, `support`, `ot:`/`o*`,
  `kb:`/`k*`, `wtdone:`; Phase 5 adds `wtok:`).
- `TelegramBotService.HandleCallbackAsync`: for data starting with `mack:` the generic empty
  `AnswerCallbackAsync` at the top is SKIPPED and `MessageAckService.HandleAsync(chatId, callbackId, deliveryId)` answers:
  - delivery not found → toast «Xabar topilmadi yoki eskirgan»;
  - `delivery.ChatId != chatId` → toast «Bu xabar sizga tegishli emas» (no change);
  - first press → `AckAt = now`, save, `EditMessageReplyMarkupAsync(chatId, TelegramMessageId, null)` (button
    removed), toast «✅ Qabul qilindi. Rahmat!»; later presses → same toast, no write.
- "Ko'rdi / qabul qildi" is ONE status (bots get no read receipts): `AckAt != null`.

**Blocking / unblocking (`TelegramBlocks`)**
- `my_chat_member` for a PRIVATE chat: `new_chat_member.status == "kicked"` → mark blocked; `"member"` → unblock
  (`BlockedAt = null`). Extend `HandleMyChatMemberAsync` (today it ignores private chats).
- Any `message` or `callback_query` from a private chat whose registrations have `BlockedAt != null` → unblock
  (one `AnyAsync` check per update, write only when needed).

**Registration (`TelegramBotService.HandleContactAsync` / `CompleteAsync`)**
- Match students by ANY of `Phone`, `ParentPhone`, `FatherPhone`, `MotherPhone` (`PhoneUtil.Key`, ≥ 7 digits).
  `CenterMeta.TelegramPhoneMatchField` is ignored from now on (column kept; its UI select is replaced by the
  info line «Bot o'quvchini 4 ta raqamning istalgani bo'yicha taniydi: o'quvchi, asosiy ota-ona, ota, ona»).
- One registration per matched student (unique `(StudentId, ChatId)` unchanged), `Relation = Detect(...)`.
  Existing registration: set `Relation` only if it is `""` (never overwrite an admin edit); clear `BlockedAt`.
- For each NEWLY created registration of a non-archived student: for every enabled `bot_connected` rule,
  enqueue ONE message to THAT chat only (not to the student's other chats), rendered for the student with
  `{qarindosh}` = `TelegramRelations.Label(relation)`, via `TelegramOutbox.Enqueue` directly
  (`DedupeKey = "bot_connected:{ruleId}:{studentId}:{chatId}"`). The functional «✅ Ro'yxatdan o'tdingiz…»
  confirmation and the login message stay as today.

### P2.5 Auto messages integration (U2b)

**Trigger catalog (`AutoMessageTriggers`)** — record gains `ScheduleMode` (from `AutoMessageSchedule.ModeOf`)
and `DefaultAck` (bool). Changes:
- `payment_debt`: description «Qarzi bor o'quvchilarga har oyning tanlangan kunida, belgilangan soatda
  eslatma. Xabar HAR FAN uchun alohida…» (keep the rest); `DefaultAck = true`.
- `birthday`: «… belgilangan soatda tabrik»; default time 09:00.
- `attendance_absent`: `DefaultAck = true`.
- New (all Telegram, audiences `parents`/`students` unless stated):

| Key | Label | Category | Tokens | Default template |
|---|---|---|---|---|
| `bot_connected` | «Botga ulanganda (01-xabar)» | Boshqa | `{ism} {fish} {qarindosh} {markaz}` | «Assalomu alaykum! {markaz} botiga ulanganingiz uchun rahmat 🙏\nEndi {ism}ning davomati, to'lovlari va natijalari haqidagi xabarlarni shu yerda olasiz.» — Audiences: none (always the chat that connected) |
| `achievement` | «Yutuq (sertifikat / IELTS / CEFR)» | O'quv jarayoni | `{ism} {fish} {yutuq} {yutuq_natija} {sana} {markaz}` | «🎉 Tabriklaymiz! {fish} — {yutuq} {yutuq_natija}. Farzandingiz bilan faxrlanamiz! {markaz}» |
| `test_score` | «Test natijasi (ota-onaga)» | O'quv jarayoni | `{ism} {fish} {test} {turi} {sana} {ball} {foiz} {orin} {guruh_orin} {reyting_orin} {guruh} {markaz}` | «📝 {test} ({turi}, {sana})\n{fish}: {ball} — {foiz}\nTestdagi o'rni: {orin}\nMarkaz reytingi: {reyting_orin}\n{markaz}» |
| `rating_top` | «Oylik TOP — tashakkurnoma» | O'quv jarayoni | `{ism} {fish} {oy} {orin} {foiz} {guruh} {markaz}` | «🏆 Tabriklaymiz! {fish} {oy} oyida test natijalari bo'yicha eng yaxshilar qatorida: o'rni {orin}, o'rtacha natija {foiz}. Rahmat! {markaz}» |
| `rating_bottom` | «Oylik quyi natija — e'tibor» | O'quv jarayoni | same as top | «Hurmatli ota-ona! {fish}ning {oy} oyidagi test natijalari ({foiz}) guruhda pastroq (o'rni {orin}). Iltimos, tayyorgarligiga e'tibor qarating; savollar bo'lsa o'qituvchi bilan bog'laning. {markaz}» |

`test_score` is distinct from the existing lead-level `test_result`. `rating_*` have `ScheduleMode =
monthly_day_time`, default day 3, time 10:00. Lead triggers are not touched.

**Token catalog (`MessageTokenCatalog`)** — add (group `event`): `{qarindosh}` «Kim (otasi/onasi/o'zi)»,
`{yutuq}` «Yutuq nomi», `{yutuq_natija}` «Yutuq natijasi», `{test}` «Test nomi», `{turi}` «Test turi
(Oylik/Oraliq)», `{orin}` «O'rin (masalan 3/18)», `{guruh_orin}` «Guruhdagi oylik o'rin», `{reyting_orin}`
«Markazdagi oylik o'rin». Existing `{ball}`, `{foiz}`, `{oy}`, `{sana}` are reused (the dispatcher passes event
values as `extra`, which override common values). Add `Example` (string) to `TokenInfo` for previews:
`{summa}` "350 000", `{sabab}` "Sababsiz", `{ball}` "25/30", `{foiz}` "83%", `{orin}` "3/18",
`{reyting_orin}` "12/430", `{yutuq}` "IELTS", `{yutuq_natija}` "7.5", `{test}` "Unit 5", `{turi}` "Oylik",
`{qarindosh}` "Otasi", others "" (filled from the sample student).

**`AutoMessageService.DispatchStudentAsync`** — Telegram branch replaced by the outbox:
- add optional `string? eventKey`, `string? sourceId` and `string? onlyRuleId` parameters (default null;
  `onlyRuleId` restricts the dispatch to that rule — used by scheduled triggers that run rule by rule:
  birthday, rating_top/bottom);
- recipients: student's registrations with `BlockedAt == null` and relation by `rule.Audience`:
  `parents` → `IsParent`; `students` → `student`; `teachers` → the group teacher's chats (existing logic);
- text = header + `MessageRender.RenderStudent(...)`; `AckRequired = rule.AckButton`;
  `AttachmentId = rule.AttachmentId`;
- `DedupeKey = $"{trigger}:{rule.Id}:{studentId}:{chatId}:{eventKey}"` when `eventKey` is given, else the row id;
- archived student rule (`ReachesArchived`) and in-app bell behaviour unchanged, except that the bell body is
  `TelegramHtml.ToPlain(body)` (no markup in `UserNotification`);
- a rendered auto message longer than 4 096 visible chars is sent as `ToPlain` cut at 4 000 chars + «…» (warning
  logged with rule id and student id — never the text);
- after `SaveChanges` → `MessageDeliveryWorker.Wake()` (inject the singleton).
`CustomReminderService` (students and teachers) and every other student-facing direct
`telegram.SendMessageAsync` (grep `TelegramRegistrations.Where(r => r.StudentId`) move to the outbox. Exceptions
kept direct: bot interactive replies (login, OTP, support), contracts (`ContractsController`), lead cards.

**`PaymentReminderService`** — new schedule:
- tick every 10 min; for each enabled `payment_debt` rule with `AutoMessageSchedule.IsDue(rule, now)` and no
  `JobRunMarker` `payment_debt:{ruleId}` for today: run the rule; per-student claim key
  `payment_debt:{ruleId}:{studentId}` (today); mark the rule done for today when no unclaimed failure remains;
  prune keys older than 2 days (unchanged policy);
- message building moves to `DebtMessages.BuildForStudentAsync(db, student, groupByName)` (public static, the
  current `BuildMessagesAsync` + `Compose` unchanged: one message per course, debt up to the current month);
- `DedupeKey = $"payment_debt:{ruleId}:{studentId}:{groupId|none}:{chatId}:{yyyy-MM-dd}"`.
- The hardcoded 09:00 odd-day schedule is deleted.

**`BirthdaySmsService`**: ticks every 10 min; each enabled birthday rule is dispatched once per day after its own
`ScheduleTime` (`DispatchStudentAsync(…, onlyRuleId: rule.Id, eventKey: yyyy-MM-dd)`; per-student key
`birthday:{ruleId}:{studentId}`; rule-level day marker `birthday:{ruleId}`).

**Auto rule API (`AutoMessagesController`)**: request/DTO + `ackButton: boolean`, `attachmentId?: string`;
validation by `ScheduleMode` (`monthly_day_time`: day 1..31 + `HH:mm`, and the server forces
`ScheduleType = "monthly"` so `CustomReminderService.IsScheduledDay` applies; `daily_time`: `HH:mm`); template validated by
`TelegramHtml` + `UnknownTokens` (400 with the list). `GET triggers` returns `scheduleMode`, `defaultAck`.
Rule create/update/delete is now audited (`AutoMessageRule`, section `messages`).

### P2.6 Achievements (U2c)

`AchievementService` (IO): create/update/delete, `QueueCongratulationAsync(achievement)` → dispatch
`achievement` trigger with `extra {yutuq, yutuq_natija, sana(dd.MM.yyyy)}`, `eventKey = achievement.Id +
":" + n` where n increments per explicit resend, `sourceId = achievement.Id`, attachment = achievement photo
if set (overrides the rule attachment). Student must be non-archived to be congratulated (400 «Arxivdagi
o'quvchiga tabrik yuborilmaydi»). Kind labels: ielts «IELTS», cefr «CEFR», certificate «Sertifikat»,
olympiad «Olimpiada», other «Boshqa». Audit `StudentAchievement` (section `students`, `studentId`).

### P2.7 API (all JSON; errors `{ message }`)

Common list shape `Paged<T> = { items: T[], total: number, page: number, pageSize: number }` (pageSize ≤ 200,
default 50).

New controller **`MessagingController`** — `[Route("api/admin/messaging")]`,
`[AdminPerm("messages.broadcast", ReadRequiresPerm = true)]` (POST → `create`, PUT → `edit`, DELETE → `delete`).

| Method | Path | Body / query | Response | Errors |
|---|---|---|---|---|
| POST | `preview` | `{ template, audience?, studentId?, attachmentId?, trigger? }` | `{ html, plain, visibleLength, mode: 'text'\|'caption'\|'separate', errors: string[], warnings: string[], sample: { studentId, fullName, relation } \| null }` | never 400 for template problems (they are in `errors`) |
| POST | `audience/count` | `{ audience: AudienceSpec, template? }` | `{ students, chats, messages, byRelation: [{ relation, chats }], notConnected, blocked, perChat: boolean, sampleNames: string[≤5] }` | 400 invalid spec |
| POST | `attachments` | multipart `file` | `{ id, kind, fileName, sizeBytes, contentType }` | 400 (see limits) |
| GET | `attachments/{id}/content` | | file stream, `Cache-Control: private, no-store` | 404 deleted/unknown |
| POST | `campaigns` | `{ kind: 'broadcast'\|'archive'\|'debt', audience, template, attachmentId?, ackRequired }` | 202 `{ id, messages, chats, students }` | 400 |
| GET | `campaigns` | `kind, from, to, page, pageSize` | `Paged<CampaignRow>` | |
| GET | `campaigns/{id}` | | `CampaignRow & { html, attachment: { id, kind, fileName } \| null, audience: AudienceSpec }` | 404 |
| GET | `campaigns/{id}/deliveries` | `status, q, page, pageSize` | `Paged<{ studentId, studentName, groupNames: string[], rows: [{ deliveryId, relation, recipientName, status, sentAt, ackAt, error }] }>` (paged by student) | 404 |
| POST | `campaigns/{id}/cancel` | | `{ cancelled }` (pending → cancelled) | 404 |
| GET | `auto-stats` | `from, to` (≤ 92 days) | `[{ date, trigger, label, total, sent, failed, blocked, acked }]` | |
| GET | `debtors` | `groupId, courseId, teacherId, minDebt, memberStatus` (`active`\|`frozen`; default both) | `[{ studentId, fullName, totalDebt, items: [{ groupId, groupName, courseName, debt }], telegram: { chats, relations: Relation[] }, lastReminder: { at, status, ackAt } \| null }]` — population = the automatic reminder's (non-archived, `Balance < 0`, not closed-group), debts per course from `DebtMessages`; `lastReminder` = latest `payment_debt`/`debt` delivery of the student | |
| GET/POST/PUT/DELETE | `templates[/{id}]` | `{ name, text, order }` | `{ id, name, text, order }` | 400 empty name/text, template errors |

`CampaignRow = { id, kind, title, textPlain (≤200), senderName, createdAt, status, total, sent, failed, blocked, pending, acked, ackRequired, hasAttachment }`.

`CampaignService.CreateAsync(db, request, actorId, actorName)` (Application, IO) implements `POST campaigns`
(validate → resolve audience → render every message → write header + rows) and is reused by the legacy adapter.
Campaign validation (400, Uzbek): text empty AND no attachment → «Matn yoki fayl kerak»; `MessageTokenCatalog.ForbiddenInManual`
→ «{link} qo'lda yuborilmaydi»; unknown tokens → «Noma'lum o'zgaruvchi: {ismi}»; `TelegramHtml` errors; 0 messages →
«Oluvchi topilmadi»; > 20 000 messages → «Juda ko'p oluvchi (20 000 dan oshmasin)»; any rendered message longer than
4 096 visible chars → «Xabar juda uzun ({n} belgi, {o'quvchi}) — 4096 dan oshmasin»; `kind=archive` with a non-archive
audience (or selected ids containing a non-archived student) → «Arxiv xabarida faqat arxivdagi o'quvchilar»;
`kind=broadcast` with archive audience → «Arxivdagilarga alohida «Arxiv» xabaridan yuboring»;
`kind=debt` audience must be active/selected (debt messages built per course via `DebtMessages`; empty template =
system text). Rendering happens at enqueue time — what was previewed is what is stored. All rows + the
`Broadcast` header are written in ONE `SaveChanges`. Audit `Broadcast` create: «Telegram xabar yuborildi:
{label}, {N} ta xabar» (+ first 120 chars of the plain template); cancel: «… bekor qilindi ({n} ta)».

Attachment limits: photo `.jpg/.jpeg/.png` ≤ 10 MB (magic `FF D8 FF` / `89 50 4E 47`); video `.mp4` ≤ 50 MB
(bytes 4–7 = `ftyp`); document `.pdf` (`%PDF`), `.docx/.xlsx/.pptx` (`50 4B 03 04`), `.doc/.xls/.ppt`
(`D0 CF 11 E0`) ≤ 50 MB. Extension, `Content-Type` and magic must all agree. Errors: «Fayl turi qo'llab-
quvvatlanmaydi (jpg, png, mp4, pdf, docx, xlsx, pptx, doc, xls, ppt)», «Rasm 10 MB dan oshmasin», «Video va
fayl 50 MB dan oshmasin», «Fayl mazmuni kengaytmasiga mos emas». Endpoint attributes
`[RequestSizeLimit(52_428_800)]`, `[RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]`. Storage
`ContentRoot/uploads/messaging/` — add this folder to the `PrivateFolderFileProvider` set in `Program.cs`
(never served statically; `uploads-security.md`).

**Student endpoints** (partial `StudentsController.Telegram.cs`, class perm `students.list`):
- `PUT api/admin/students/{id}/telegram-links/{regId}` `{ relation }` → `LinkDto` (edit). 400 unknown relation.
  Audit `Student`: «Telegram ulanishi turi o'zgardi: {name} — Otasi → Onasi».
- `TelegramLinks.LinkDto` gains `Relation`, `Blocked` (bool), `BlockedAt` (string?).
- `GET api/admin/students/{id}/telegram-deliveries?limit=50` → `[{ id, createdAt, sentAt, trigger, label, relation,
  status, ackAt, textPlain (≤300) }]`; method attribute `[AdminPerm("students.list", "messages.broadcast", ReadRequiresPerm = true)]`.

**Achievements** — `StudentAchievementsController` `[Route("api/admin/achievements")]`,
`[AdminPerm("students.list", ReadRequiresPerm = true)]`:
`GET ?studentId=&kind=&from=&to=` → `AchievementDto[]`; `POST { studentId, kind, title, result, date, note,
attachmentId?, congratulate }` → `AchievementDto & { messages }`; `PUT {id}` (same minus congratulate);
`DELETE {id}`; `POST {id}/congratulate` → `{ messages }`; `GET {id}/photo` → image.
`AchievementDto = { id, studentId, studentName, kind, kindLabel, title, result, date, note, hasPhoto, congratulatedAt, createdAt, createdByName }`.
Validation: title 1..200, result ≤ 50, date valid and ≤ today, kind known, attachment must be a `photo`.
Portal (partial `StudentPortalController.Achievements.cs`, roles student/parent/admin via `TargetAsync`):
`GET api/student/achievements?studentId=` and `GET api/student/achievements/{id}/photo` (ownership checked).

**Legacy** `POST api/admin/messages/broadcast` stays as an adapter over `CampaignService` (scope class/all/selected
mapped to `AudienceSpec`, ack off) returning the old `BroadcastDto`; the new client does not call it.

### P2.8 Client (U2d)

Messages page `/admin/messages` (`messages.broadcast`) — tabs via `?tab=` (`tabFromUrl`):
«Xabar yuborish» (`yuborish`, default) · «Qarzdorlar» (`qarzdorlar`) · «Avto xabarlar» (`avto`) ·
«Shablonlar» (`shablonlar`) · «Tarix» (`tarix`). Catalog label of `messages.broadcast` becomes «Telegram xabarlar».

**Composer** (`pages/admin/messages/TelegramComposer.tsx`, replaces `UnifiedComposer` on this page):
1. «Kimlarga» segmented: «Faol o'quvchilar» · «Arxiv» · «Tanlangan» · «O'qituvchilar».
   Faol: «Guruh» (multi), «Kurs» (multi), «O'qituvchi», holat chips «Aktiv» ✓ «Sinov» ✓ «Muzlatilgan»,
   toggle «Faqat qarzdorlar». Arxiv: «Arxivlangan sana» from–to, «Kurs», «Sabab» search. Tanlangan: student
   search picker (opened pre-filled from other pages). O'qituvchilar: multi-select.
   Relation chips: «Otasi» «Onasi» «Ota-onasi» «O'quvchining o'zi» «Boshqa» + presets «Barcha ota-onalar»,
   «O'quvchilar».
2. Editor (`components/messaging/MessageEditor.tsx`): toolbar «Qalin» (wraps selection in `<b>…</b>`), «Kursiv»
   (`<i>`), «Tagiga chizilgan» (`<u>`), «Havola» (prompt URL → `<a href="URL">…</a>`), «Formatni tozalash»;
   token chips; «Shablondan» picker.
3. «Rasm / video / fayl biriktirish» (one file; progress; thumbnail for photos; name + size otherwise; «Olib
   tashlash»). Hint when caption mode would be `separate`: «Matn izoh uchun uzun — fayl va matn ikki alohida
   xabar bo'lib ketadi».
4. Checkbox «"Qabul qildim" tugmasi» (default on).
5. Right column: Telegram-like preview bubble (`components/messaging/TelegramPreview.tsx`), POST `preview`
   debounced 400 ms; «Namuna: <name> ▾» changes the sample; errors in red under the editor.
6. Footer count (POST `audience/count`, debounced): «Yuboriladi: 312 ta xabar · 287 ta chat (otasi 140, onasi
   120, o'quvchi 27) · Ulanmagan: 58 o'quvchi · Bloklagan: 6». «Yuborish» disabled when 0 messages or errors →
   confirm dialog repeating the numbers → POST `campaigns` → navigate to «Tarix» with the campaign report open.
   Preview HTML is rendered with `lib/telegramHtml.ts` (whitelist → React elements; never `dangerouslySetInnerHTML`).
- `TelegramSendModal` (compact composer: fixed `selected` audience + relation chips + editor + attachment + ack +
  preview + count) replaces the old SMS modal entry points: student profile «Xabar yuborish», students lists
  bulk action, group page «Guruhga xabar», archive list bulk «Xabar yuborish» (kind `archive`), attendance page.
- «Qarzdorlar» tab (`DebtorsTab.tsx`): filters «Kurs», «Guruh», «O'qituvchi», «Qarz kamida» (MoneyInput),
  holat; columns ☐ · «F.I.Sh» · «Kurslar bo'yicha qarz» (chips "Ingliz tili — 350 000") · «Jami qarz» ·
  «Telegram» (relation icons; red «Ulanmagan») · «Oxirgi eslatma» (date + ✓✓ «Qabul qildi» / ✓ «Yuborildi» /
  ✗); bulk «Eslatma yuborish» → side panel (template: «Tizim matni» or saved template; ack; preview; count) → `kind=debt`.
- «Tarix»: campaigns table (date, kind badge, audience, sender, total/sent/failed/blocked/acked, status);
  row → `CampaignReportModal` (status filter chips, search, per-student rows with relation chips and icons:
  ✓ «Yuborildi», ✓✓ «Qabul qildi», ⛔ «Bloklagan», ✗ «Xato», ⏳ «Navbatda»; «Bekor qilish» while pending; polls
  every 3 s while `status != done`). Sub-tab «Avto xabarlar»: `auto-stats` table by day × trigger.
- «Avto xabarlar»: rule card gets the formatting editor + preview, «Rasm/fayl» attachment, «"Qabul qildim"
  tugmasi» (pre-filled from `defaultAck` for new rules), schedule fields by `scheduleMode`
  («Oyning qaysi kuni» 1–31 with note «31 — qisqa oylarda oyning oxirgi kuni», «Soat»).
- Student profile → «Aloqa»: `TelegramLinksSection` shows relation (select, `students.list:edit`) and a red
  «Botni bloklagan» badge; new `TelegramDeliveriesSection` («Telegram xabarlari», last 50: date, type, relation,
  status icon, «Qabul qildi HH:mm»). New tab «Yutuqlar» (after «Sertifikatlar»): list + «Yutuq qo'shish» modal
  (Turi, Nomi, Natija, Sana, Izoh, Rasm, checkbox «Ota-onaga tabrik yuborish» default on).
- Student cabinet `/student/certificates`: section «Yutuqlarim» (cards with photo, title, result, date).
- Settings → Xabar kanallari → Telegram: TelegramPhoneMatchField select removed (info line, P2.4).

### P2.9 Audit

`AuditSections.All` gains `new("messages", "Xabarlar")` (before `settings`); map `Broadcast`, `AutoMessageRule`,
`MessageTemplate` → `messages`; `StudentAchievement` → `students`.

### P2.10 MCP impact (U2m — list)

`messages_history` (campaign kind/status/sent/failed/blocked/acked), new `message_deliveries` (by student,
campaign, trigger, date; never chat id), `auto_message_rules` (ack, attachment flag, schedule), `student_profile`
(Telegram link counts by relation, blocked count), new `student_achievements`.

### P2.11 Tests (backend xUnit unless noted)

- `TelegramHtmlTests`: student name `Ali <b>` renders escaped; `<strong>` → `<b>`; `<script>` escaped; unclosed
  `<b>` error; `javascript:` link error; `Q&A` → `Q&amp;A`; `&amp;` kept; `ToPlain` decodes; `VisibleLength`.
- `TelegramRelationsTests`: father before student when the same number; ParentPhone = MotherPhone → mother;
  no match → other; < 7 digits ignored.
- `MessageAudienceTests`: active excludes archived and closed-group students; status filter; relation filter;
  blocked excluded and counted; archive only archived; selected exact; per-chat dedupe when no student token;
  `audience/count.messages` == rows created by `campaigns` for the same spec.
- `MessageDeliveryWorkerTests` (fake `ITelegramSender`): ok → sent + message id; blocked → status + every
  registration of the chat has `BlockedAt`; 429 → pending with `NextAttemptAt`; 3 failures → failed; startup
  `sending` → `unknown` not resent; expired not sent; `BadMarkup` → plain retry; per-chat spacing; campaign
  becomes `done`.
- `MessageAckTests`: right chat sets `AckAt` once; wrong chat no change; unknown id no throw; repeated press idempotent.
- `TelegramRegistrationTests`: matches father/mother phones; one registration per sibling; `bot_connected`
  queued once (dedupe); existing relation not overwritten; unblock on contact.
- `PaymentReminderScheduleTests`: rule day 10 09:00 → runs only on the 10th after 09:00; 31 → Feb 28/29; two rules
  same day both run; restart does not duplicate (claim keys); amounts unchanged vs `AutoMessageTests` §7.
- `MessageAttachmentTests`: extension/type/magic mismatch rejected; size limits per kind; stored name is a GUID.
- `MessagingRbacTests`: staff without `messages.broadcast` → 403 on every endpoint (incl. GETs); preview needs create.
- `AuditSectionsTests` updated for the new types and section.
- Client (vitest): `lib/__tests__/telegramHtml.test.ts` (whitelist renderer, link scheme), `relations.test.ts`.

### P2.12 Risks

Telegram rate limits (worker throttle + 429 handling); HTML rejected by Telegram (sanitiser + plain fallback);
duplicate messages during the switch (every student-facing direct send must move to the outbox — grep list in
P2.5); a 50 MB upload on a 1 GB server (stream, never buffer); old templates containing `<`/`&` (sanitiser
escapes them — verify all existing rules with `preview` after deploy); disabling SMS-only rules (release note
must list them).

### P2.13 Acceptance criteria

1. Typing `<b>Salom</b> {ism}` shows bold "Salom" + the sample's first name; a student named `Ali <b>` arrives
   in Telegram as literal text.
2. The count line equals the number of `MessageDelivery` rows the send creates.
3. Photo (jpg/png ≤ 10 MB), video (mp4 ≤ 50 MB) and document (pdf/docx/xlsx ≤ 50 MB) are delivered; text ≤ 1024
   visible chars is the caption, longer text arrives as a second message; wrong files are rejected with the
   Uzbek reason.
4. With «"Qabul qildim" tugmasi» on, pressing the button shows «✅ Qabul qilindi. Rahmat!», removes the button,
   and the campaign report and the student's «Telegram xabarlari» show «Qabul qildi» with time.
5. A chat that blocked the bot gets status «Bloklagan»; its registrations disappear from later counts
   («Bloklagan: N»); after the user sends `/start` they are counted again.
6. Father, mother and the student share their contacts → 3 registrations (Otasi/Onasi/O'zi); each chat gets the
   01 thank-you exactly once.
7. «Barcha ota-onalar» never includes archived, closed-group, blocked or student-relation chats; «Arxiv» includes
   only archived students and cannot be mixed with active ones.
8. A `payment_debt` rule set to day 10, 10:30 sends on the 10th after 10:30, once per student per course, also
   across a restart.
9. Restarting during a campaign continues `pending` rows; rows that were `sending` become `unknown` and are not resent.
10. Every new endpoint returns 403 for a staff user without the required key.
11. Creating an achievement with «Ota-onaga tabrik yuborish» queues the message; it shows in the profile and the cabinet.

---

## 4. Phase 3 — Attendance

### P3.1 One definition (U3a) — `AttendanceRules` (pure, `AttendanceRulesTests`)

```csharp
public enum LessonMark { NotHeld, Blocked, Unmarked, Present, Late, AbsentExcused, AbsentUnexcused }
public static LessonMark Classify(bool conducted, bool attendanceTaken, bool blocked, bool beforePresentDefault,
                                  JournalEntry? e, AbsenceReason? reason)
```
Order: `blocked` → Blocked; reason != null → `IsLate` ? Late : (`IsExcused` ? AbsentExcused : AbsentUnexcused);
`e.Present || e.Grade != null || e.Mastery != null` → Present; `!conducted` → NotHeld;
`attendanceTaken && !beforePresentDefault` → Present; else Unmarked.
Tally: **Held = Present + Late + AbsentExcused + AbsentUnexcused; Attended = Present + Late;
percent = round(Attended × 100 / Held)** (Held 0 → null). Unmarked, NotHeld, Blocked are excluded.
So: **late = attended**; a conducted lesson whose attendance was never taken leaves unmarked students OUT of the
percentage (they are neither present nor absent).

Switch these call sites to `Classify`/`Tally` (grep `IsLate` and `ReasonId != null` for any other):
`StudentJournalBuilder` (BuildCells → Cell gets `Mark`; summaries), `StudentProfileBuilder`,
`StudentReportBuilder`, `TeacherHomeService` (students list counts), `StudentAttendanceController`,
`StudentPortalController` attendance endpoints, `ClassAnalyticsController` performance, `Analytics.cs`,
`GroupSnapshotBuilder`/`TeacherSnapshotBuilder` (AI input). `RatingService` is NOT touched here (U4b rewrites it).
Client: `pages/admin/classes/detail/shared.ts` `autoPresent` takes `attendanceTaken` instead of `conducted`;
`GroupAttendanceTab`/journal tab counts late as attended; `GroupJournalDto` gains `AttendanceTakenDates: List<string>`
(appended last parameter, built like `ConductedDates` from `LessonNote.AttendanceTaken`).

**`LessonNote` (partial)**: `AttendanceTakenAt` (DateTime?, local) — time of the FIRST attendance mark (Present
or ReasonId, single or bulk) of the lesson; `AttendanceTakenById` (string?, AppUser id). `AttendanceTaken` (bool,
existing) now means "attendance complete": set by bulk attendance (existing) OR by `SetEntryAsync` when, after
saving, every student expected on that date has Present/ReasonId/Grade for that lesson. Expected = live or
ended-after-date memberships with `StudentJournalBuilder.ExpectedOn(m, date)`, student not archived, and — on the
TEACHER path only, and only when a payment-gate setting is on — not hidden by `JournalPolicy.PaymentGate`
(the teacher cannot see them; bulk already skips them). Grades alone never set `AttendanceTakenAt`.
"Attendance taken" for reminders, fines, monitor and notices = **any `LessonNote` of (ClassId, Date) with
`AttendanceTakenAt != null`** (subject/period agnostic). `LessonAttendanceReminderService.ConductedAsync` and
`DashboardController` `AttendanceDone` switch to this (today they use `Conducted`, which a single grade sets).

**`AbsenceReason` (partial)**: `IsExcused` (bool, default false). Rules (`AbsenceReasonRules`): `IsLate && IsExcused`
→ 400 «"Kech keldi" sababi bir vaqtda "sababli" bo'la olmaydi»; `IsExcused` may change even when used (it only
affects analytics; the audit row records it). Seed for new databases: «Kasal» and «Sababli» excused, «Sababsiz»
not. Migration SQL:
```sql
UPDATE "AbsenceReasons" SET "IsExcused" = TRUE
 WHERE "IsLate" = FALSE AND lower("Name") NOT LIKE '%sababsiz%';
```
UI: Sozlamalar → O'quv → Sabablar → «Davomat sabablari» rows get checkbox «Sababli (uzrli)» next to «Kech keldi».
`PUT settings/absence-reasons` and its GET carry `isExcused`. (U3m: MCP `AttendanceTools` stops using the
`Name.Contains("sababsiz")` heuristic and reads `IsExcused`.)

### P3.2 Settings (CenterMeta partial, U3a adds columns; U3b/U3c use them)

| Column | Type | Default | Range / values |
|---|---|---|---|
| `AttendanceDisciplineEnabled` | bool | false | master switch for reminders/escalation/fines |
| `AttendanceDeadlineMinutes` | int | 15 | 5..120 — attendance is "on time" if taken ≤ lesson start + N |
| `AttendanceReminderMinutes` | string | `"5,10"` | ≤ 3 ascending ints, each 1..deadline; `""` = no reminders |
| `AttendanceEscalateEnabled` | bool | true | message admins at the deadline |
| `AttendanceFineEnabled` | bool | false | |
| `AttendanceFineAmount` | decimal(18,2) | 0 | 0..10 000 000; must be > 0 when fine enabled |
| `AbsentNoticeDelayMinutes` | int | 1 | 0..180 — absent notice goes at lesson end + N |
| `AbsentNoticeApproval` | string(16) | `late_only` | `auto` · `manual` · `late_only` |
| `AbsenceStreakReasonId` | string? | null | id of the seeded contact reason (P3.5) |

API (new `AttendanceSettingsController`, `[Route("api/admin/attendance/settings")]`, `[AdminPerm("classes")]` —
same gate as the journal/salary policy): `GET` → `{ disciplineEnabled, deadlineMinutes, reminderMinutes: number[],
escalateEnabled, fineEnabled, fineAmount, absentNoticeDelayMinutes, absentNoticeApproval, warnings: string[] }`;
`PUT` same body (minus warnings) → same; 400 per rule above (`AttendanceSettingsRules.Validate`, pure).
`warnings`: when an enabled `lesson_attendance` rule with scope `lesson_start` exists and discipline is on →
«Avto xabarlarda «Davomat eslatmasi» qoidasi ham yoqilgan — o'qituvchi ikki marta eslatma olishi mumkin».
Audit: `SettingsAudit.Record<CenterMeta>(…, "Davomat intizomi", withValues: true, labels …)`.
UI: Sozlamalar → O'quv → «Jurnal va davomat» gains rows «Davomat intizomi» («Eslatma, jarima va
administratorga xabar», modal `AttendanceDisciplineModal`) and «Davomat xabarlari» («Ota-onaga "kelmadi"
xabari vaqti va tasdiqlash», modal `AbsentNoticeSettingsModal`); both `perm: 'classes', action: 'edit'`.
Modal texts: «Davomat dars boshlangandan keyin necha daqiqada olinishi shart», «O'qituvchiga eslatma (daqiqa,
vergul bilan)», «Muddat o'tsa administratorlarga xabar», «Jarima yozilsin», «Bir dars uchun jarima (so'm)»,
«Dars tugagandan keyin necha daqiqada yuborilsin», approval radio «Avtomatik» / «Har birini menejer tasdiqlaydi» /
«Faqat davomat kech olinganda tasdiqlash (tavsiya)».

### P3.3 Absent-notice queue with manager hold (U3b)

**`AttendanceNotifyItem` (partial)**

| Column | Type | Default | Notes |
|---|---|---|---|
| `SendAfter` | DateTime | =CreatedAt | earliest send time |
| `LessonStart`, `LessonEnd` | string(5) | `""` | lesson times on that date (moved lessons use their own time) |
| `TakenLate` | bool | false | attendance first taken after start + deadline |
| `DecidedById`, `DecidedByName` | string? | | manager |
| `DecidedAt` | DateTime? | | |
| `DecisionNote` | string(300)? | | |
| `CancelReason` | string(32)? | | `manager` · `changed` (journal no longer says absent/present) · `archived` |

Statuses (concurrency token): `pending` (auto at `SendAfter`), `awaiting` (needs approval), `held` (stopped by a
manager), `approved`, `sending`, `done`, `failed`, `unknown`, `expired`, `cancelled`.
Migration SQL: `UPDATE "AttendanceNotifyItems" SET "SendAfter" = "CreatedAt";`

Creation (`AttendanceNotify`):
- ALL attendance messages go through the queue — the single-cell paths (`JournalController.SetEntry`,
  `TeacherPortalController` journal PUT) stop dispatching in the request and call
  `AttendanceNotify.EnqueueSingleAsync(db, req, newAbsence, wasPresent)` + `SaveChanges` + `Wake()`.
- Only lessons dated TODAY (centre time) are enqueued; corrections of past days never message parents.
- Lesson times: `AttendanceWatch.LessonsOn([group], moves, assignments, date)` → first lesson of the day.
- `absent`: `SendAfter = AbsentNoticeRules.SendAfter(date, lessonEnd, lessonStart, markedAt, delay)` =
  date+lessonEnd+delay; unknown end → markedAt + delay; never earlier than markedAt.
  Status by `AbsentNoticeApproval`: `auto` → pending; `manual` → awaiting; `late_only` → awaiting if
  `TakenLate` else pending. `TakenLate = AttendanceTakenAt > date+lessonStart+DeadlineMinutes` (no start → false).
- `present`: `SendAfter = now`, status `pending`, never needs approval.

Worker (`AttendanceNotifyWorker`): processes `(pending|approved) && SendAfter <= now`; marks `expired` any
`pending|awaiting|held|approved` with `now > SendAfter + 6 h`; before sending re-checks the journal entry:
absent item → entry still has a non-late reason, present item → entry still `Present`; otherwise `cancelled`
(`CancelReason = changed`); archived student → `cancelled` (`archived`). Sending = existing dispatch through
`AutoMessageService` with `eventKey = item.Id`, `sourceId = item.Id` (→ outbox after U2b).
**Default if nobody acts:** `pending` items go out at `SendAfter`; `awaiting`/`held` items never go out and expire.
When `awaiting` items are created for a lesson, managers (superadmin, admin, staff with `students.notices`)
receive ONE Telegram message per lesson (direct send; `LessonReminderMark` key `notice-await:{groupId}:{date}`)
and a bell (`NotificationStore`): «🕓 {guruh} ({sana}) — {n} ta «kelmadi» xabari tasdiq kutmoqda: davomat kech
olingan ({vaqt}). Tekshiring: https://{App:Host}/admin/nazorat/davomat-xabarlari?date={sana}» (link omitted when
`App:Host` is empty).

Transitions (`AbsentNoticeRules.CanApply(status, action)`, pure): hold from pending/awaiting/approved; approve
from pending/awaiting/held; cancel from pending/awaiting/held/approved; anything else → 409
«Xabar allaqachon yuborilgan yoki yopilgan».

API — new `AttendanceNoticesController` `[Route("api/admin/attendance-notices")]`,
`[AdminPerm("students.notices", ReadRequiresPerm = true)]`:
- `GET ?date=&status=&groupId=&teacherId=` → `{ date, counts: { pending, awaiting, held, approved, sent, cancelled,
  expired }, lessons: [{ groupId, groupName, courseName, teacherName, lessonStart, lessonEnd, attendanceTakenAt,
  takenLate, items: [{ id, kind, studentId, studentName, reasonName, isExcused, status, sendAfter, decidedByName,
  decidedAt, note, delivery: { sent, failed, blocked, acked } | null }] }] }` (delivery from `MessageDelivery.SourceId`).
- `PUT {id}/hold|approve|cancel` `{ note? }` → item; 409 invalid transition / concurrency («Holat o'zgardi,
  sahifani yangilang»).
- `PUT bulk` `{ ids, action, note? }` → `{ changed, skipped }`.
Audit type `AttendanceNotice` (section `classes`), `EntityId = "{groupId}:{itemId}"`, `studentId`; summaries
««Kelmadi» xabari to'xtatildi / tasdiqlandi / bekor qilindi: {o'quvchi}, {guruh}, {sana}» (+ note).
New page key `students.notices` («Davomat xabarlari nazorati») — catalog `students.pages`.
Page `/admin/nazorat/davomat-xabarlari` (`pages/admin/nazorat/AbsentNoticesPage.tsx`), menu Nazorat → «Davomat
xabarlari» (`perm: 'students.notices'`): date picker (default today), status chips with counts, cards per lesson
(header: group, teacher, time, «Davomat: 14:12» green or «Davomat kech: 14:40» amber), rows with checkbox,
student, reason, status badge, «Yuboriladi: 15:31», row actions «To'xtatish» / «Tasdiqlash» / «Bekor qilish»,
bulk bar, polling every 30 s. Status labels: pending «Rejalashtirilgan», awaiting «Tasdiq kutmoqda», held
«To'xtatilgan», approved «Tasdiqlangan», sending «Yuborilmoqda», done «Yuborildi», failed «Xato», unknown
«Noma'lum», expired «Muddati o'tdi», cancelled «Bekor qilindi».

### P3.4 Discipline: reminders, escalation, fine (U3c)

**`TeacherAttendanceFine`** (new, table `TeacherAttendanceFines`)

| Column | Type | Notes |
|---|---|---|
| `Id` | string(36) | PK |
| `TeacherId` | string(36) | teacher responsible that day (substitute if assigned — `LessonAttendanceReminderService.RecipientTeacherId`) |
| `GroupId` | string(36) | |
| `LessonDate` | string(10) | **unique (`GroupId`, `LessonDate`)** — at most one fine per group per day |
| `Month` | string(7) | `LessonDate[..7]` (salary attribution) |
| `LessonStart` | string(5) | |
| `Deadline` | DateTime | lesson start + DeadlineMinutes |
| `Amount` | decimal(18,2) | snapshot of `AttendanceFineAmount` |
| `Status` | string(16) | `active` · `waived` |
| `AttendanceTakenAt` | DateTime? | snapshot, updated if attendance is taken later the same day (information only) |
| `CreatedAt` | DateTime | |
| `WaivedAt`, `WaivedById`, `WaivedByName`, `WaiveReason` | | |
Backup: included (money).

**`AttendanceDisciplineService`** (hosted, every 60 s, only when `AttendanceDisciplineEnabled`): lessons of TODAY
via `LessonAttendanceReminderService.LessonsTodayAsync(db, day)` (visible groups; moved lessons at their new
time; substitutes); YESTERDAY's lessons are scanned only for a fine that was missed during downtime (no reminders,
no escalation, no messages for yesterday). Lessons without a start time are skipped (fines and reminders need a
time). On every tick, an `active` fine of today whose lesson now has `AttendanceTakenAt` gets that value copied
once (information only). Decisions are pure
(`AttendanceDiscipline.Decide(lesson, now, takenAt, settings) → { remindAt[], deadline, fineDue, escalateDue }`):
- reminders (today only): at start + m for each m in `AttendanceReminderMinutes` (10-min window), if not taken →
  teacher (Telegram chats of the teacher + bell): «⏰ {guruh} ({kurs}) — dars {HH:mm} da boshlandi. Davomatni
  kiriting.» + when fine enabled «\n{muddat} gacha olinmasa {summa} jarima yoziladi.»; key `disc:rem:{groupId}:{date}:{m}`.
- at the deadline (or first tick after it), if not taken ON TIME (`takenAt == null || takenAt > deadline`):
  - fine (if enabled and amount > 0 and the lesson has a teacher): insert row (unique key protects) →
    teacher message «❗️ {sana} — {guruh}: davomat {muddat} gacha olinmadi. {summa} jarima yozildi. E'tirozingiz
    bo'lsa, administratorga murojaat qiling.»; key `disc:fine:{groupId}:{date}`;
  - escalation (if enabled): admins (superadmin, admin, staff with `classes.notAttended`; Telegram via
    `TelegramRegistration.UserId` + bell) «⚠️ Davomat olinmadi: {guruh} — {o'qituvchi}, dars {HH:mm}.» (+ « Jarima:
    {summa}.»); key `disc:esc:{groupId}:{date}`.
- Attendance taken AFTER the deadline still gets the fine (the rule is "during the lesson"); entry is never blocked.
Admin waives a fine individually or for a whole day (holiday: the project has no holiday calendar).

**Salary (`SalaryLedger.BuildAsync`)**: load `active` fines of the teacher for the ledger months; per month
`applied = min(sumFines, max(0, baseExpected − deduction))`; `expected = baseExpected − deduction − applied`;
`PotentialExpected` and `CollectedExpected` also minus `applied` (floored 0). DTOs (append last, with defaults):
`MonthSalaryDto.AttendanceFines` (decimal), `.AttendanceFineCount` (int); `SalaryLedgerDto.TotalAttendanceFines`;
`SalaryReportRowDto.AttendanceFines`. Fines are independent of the existing `SalaryRequireJournal` proportional
deduction (both can apply; the settings modal says so).

API — new `AttendanceFinesController` `[Route("api/admin/attendance-fines")]`,
`[AdminPerm("finance.main", ReadRequiresPerm = true)]`:
`GET ?month=&teacherId=&status=` → `{ rows: [{ id, teacherId, teacherName, groupId, groupName, lessonDate,
lessonStart, deadline, amount, status, attendanceTakenAt, waivedByName, waivedAt, waiveReason }], totalActive,
countActive }`; `PUT {id}/waive { reason }` (1..300 chars) · `PUT {id}/restore` · `PUT waive-day { date, reason }`
→ `{ waived }`. Teacher portal (partial `TeacherPortalController.Fines.cs`): `GET api/teacher/attendance-fines?month=`
(own rows, no actions). Audit type `TeacherAttendanceFine` (section `teachers`, `teacherId`): created (system actor
«Tizim»), waived, restored, day-waived.
UI: Moliya → «Jarima» page gains tabs «Jarimalar» (existing list) and «Davomat jarimalari» (`AttendanceFinesTab`:
month, teacher, status; «Bekor qilish» with reason; «Kunni bekor qilish»); teacher profile → Maosh and the
teacher portal salary page show «Davomat jarimasi: −X so'm (N ta dars)» with the date list;
`TeacherSalaryDetailModal` shows the line.

### P3.5 Streaks, red list, contact requests, profile (U3d)

**`AbsenceStreaks`** (pure): input = ordered marks of one (student, group); walk from the latest held lesson
backwards: count `AbsentUnexcused`, skip `Unmarked`/`NotHeld`, stop at `Present`, `Late` or `AbsentExcused`.
Output `(Count, StartDate, Dates[])`. Level = 5 if Count ≥ 5, 3 if ≥ 3, else none. Per (student, group) —
different groups never mix.

**`AbsenceStreakAlert`** (new, table `AbsenceStreakAlerts`): `Id`, `StudentId`, `GroupId`, `StreakStart`
(first absent date of the streak), `Level` (3|5), `ContactRequestId` (string?), `CreatedAt`; unique
`(StudentId, GroupId, StreakStart, Level)`. Backup: excluded (derived).

**`AbsenceStreakService`** (hosted, every 30 min between 08:00 and 22:00, plus once 2 min after startup):
batch-load (one query per table) conducted `LessonNotes` of the last 60 days for non-archived groups, the
attendance-bearing `JournalEntries` for those lessons, live memberships, reasons; classify each (membership,
lesson date) with `AttendanceRules.Classify` using `StudentJournalBuilder.BoundsOf(membership)` for `blocked` and
`PresentDefaultFrom` (identical to the profile); for every (student,
group) with level ≥ 3 insert missing alert rows (both 3 and 5 when Count ≥ 5). Contact handling — at most ONE
action per student per run, only if the streak's last absence is ≤ 14 days old and the student is not archived:
- no open `ContactRequest` → `ContactQueueService.Add(student, reasonId = AbsenceStreakReasonId (if it still exists),
  reasonLabel «Ketma-ket darsga kelmadi», note «{guruh}: ketma-ket {n} ta dars sababsiz qoldirildi ({sanalar dd.MM})»,
  due "", actor "", «Tizim»)` and store its id on the alert rows;
- open request exists → add `ContactAttempt` type `note` with the same text (does not change reports) and store
  the request id. Never a second open request (`contacts.md` §4).
Startup seed `streak-reason-2026-10`: create `ActionReason { Category = "contact", Label = "Ketma-ket darsga kelmadi" }`
if no reason with that label exists in that category; save its id in `CenterMeta.AbsenceStreakReasonId`.
`KpiMetricsService.AbsenceStreaksAsync` switches to `AbsenceStreaks` (unique students with level ≥ 3 / exactly 2).

API: new `AttendanceRedListController` `[Route("api/admin/student-attendance/red-list")]`,
`[AdminPerm("students.attendance", ReadRequiresPerm = true)]`: `GET ?min=3&groupId=&teacherId=` →
`[{ studentId, studentName, groupId, groupName, teacherName, streak, level, dates: string[], lastDate,
contact: { requestId, status } | null }]` (DataCache 5 min, deps JournalEntry, LessonNote, StudentGroup, Student,
AbsenceReason, ContactRequest).
Profile: partial `StudentsController.Absences.cs`: `GET api/admin/students/{id}/absences?from=&to=` (default last
180 days, max 366) → `{ from, to, counts: { held, attended, present, late, absentExcused, absentUnexcused, unmarked,
percent }, byReason: [{ reasonId, name, isExcused, isLate, count }], items: [{ date, groupId, groupName, courseName,
reasonName, kind: 'late'|'absent_excused'|'absent_unexcused' }] }` (items newest first; built on
`StudentJournalBuilder` cells across all memberships incl. closed ones within their bounds).
UI: «O'quvchilar davomati» page (`/admin/students/davomat`) gains tab «Qizil ro'yxat» (`?tab=qizil`): rows with
red badge «5+» / amber «3+», student (link), group, teacher, dates, contact status link to «Bog'lanish kerak».
Profile → «Baholar va davomat» gets section «Qoldirilgan darslar» at the top: period chips «Shu oy» «3 oy»
«6 oy» «1 yil», counters (Darslar · Keldi · Kech · Sababli · Sababsiz · %), flat list (date, group, reason badge).
Menu: «Bog'lanish kerak» moves from Future → O'quvchilar into the main «O'quvchilar» group after «Qabul cheklisti».

### P3.6 MCP impact (U3m — list)

`attendance_daily`, `student_attendance`, `attendance_not_taken` (unified marks, `AttendanceTakenAt`),
`absence_reasons` (`isExcused`), new `attendance_red_list`, new `attendance_notices` (counts/status, no chat ids),
new `teacher_attendance_fines` (gate `t.CanReadFinance`), `teacher_salaries` (fine lines).

### P3.7 Tests

- `AttendanceRulesTests`: late = attended; graded = present; attendance-taken + no mark = present; conducted only =
  unmarked (excluded); excused vs unexcused; blocked; before `PresentDefaultFrom` = unmarked.
- `AttendanceTakenTests`: first single mark sets `AttendanceTakenAt` once; completion sets `AttendanceTaken`;
  payment-hidden students ignored on the teacher path; grade-only does not set it; bulk sets both.
- `AbsenceReasonExcusedTests`: late+excused rejected; excused flag editable while used; backfill SQL semantics.
- `AbsentNoticeTests`: SendAfter = end + delay; marked after the end → immediate; `late_only` → awaiting; manual →
  awaiting; nobody acts → pending sent, awaiting expires; teacher fixes absent → cancelled `changed`; transitions
  table; past-day lesson not enqueued; present immediate.
- `AttendanceDisciplineTests` (pure + service with fake sender and fixed clock): reminders at +5/+10 only when not
  taken; taken at +12 → no fine; at +16 → fine; substitute fined; restart does not duplicate (marks + unique);
  yesterday processed after downtime; no-time lesson skipped; disabled → nothing.
- `SalaryLedgerFineTests`: active fines reduce Expected; waived do not; capped at base; month by lesson date.
- `AbsenceStreaksTests`: U,U,U → 3; U,E,U,U → 2; U,(unmarked),U,U → 3; present breaks; groups separate.
- `AbsenceStreakServiceTests`: one request per student; open request → note; closed then 5 → new request; stale
  (> 14 days) → no request; reruns idempotent.
- RBAC: `students.notices`, `finance.main` (fines), `students.attendance` (red list), `classes` (settings).
- Client: `autoPresent` uses attendanceTaken; late counted attended.

### P3.8 Risks

Retroactive percentage change (unmarked excluded; late now attended) — release note; fines on holidays (bulk
waive); teachers marking cell by cell never reaching "complete" (discipline uses `AttendanceTakenAt` = first mark,
so they are not fined); first streak run creating many contact requests (14-day freshness limit); double reminders
with legacy `lesson_attendance` rules (warning).

### P3.9 Acceptance criteria

1. Lesson 14:00–15:30, delay 1: an absence marked at 14:10 reaches the parent at 15:31 (±1 min), not before.
2. Before 15:31 a manager can stop, approve or cancel it; stopped/awaiting notices never go out unless approved and
   show «Muddati o'tdi» after 21:31.
3. `late_only`, deadline 15: attendance first taken at 14:40 → its absent notices are «Tasdiq kutmoqda» and
   managers get one Telegram alert.
4. Teacher changes «kelmadi» to «keldi» before 15:31 → notice «Bekor qilindi».
5. Discipline on, reminders "5,10", deadline 15, fine 20 000: no attendance → teacher reminders 14:05 and 14:10;
   at 14:15 fine row + teacher notice + admin escalation; «Davomat jarimalari» lists it; the teacher's month
   salary drops by 20 000; «Bekor qilish» restores it.
6. Attendance first marked at 14:12 → no fine; at 14:16 → fine.
7. Late arrivals count as attended in profile, cabinet, teacher list and group tab percentages.
8. After migration «Sababsiz» is unexcused, «Kasal»/«Sababli» excused; late+excused cannot be saved.
9. Three consecutive unexcused held lessons in one group → red list row + exactly one «Bog'lanish kerak» request
   («Ketma-ket darsga kelmadi»); the 5th → a note on the open request; an excused absence breaks the streak.
10. Profile «Qoldirilgan darslar» lists every missed date with its reason; the counters match the list.

---

## 5. Phase 4 — Tests & rating

### P4.1 Data model

**`TestResult` (partial)**: `Kind` string(16) — `monthly` · `interim` (labels «Oylik», «Oraliq»);
`ResultsSentAt` (DateTime?), `ResultsSentById` (string?). The existing `QuestionCount` column is reused as
«Savollar soni» for result-only tests; `MaxScore = QuestionCount` for every test created from now on; a score is
the SOLVED count; percent = `Score / MaxScore × 100` (one input — owner decision). Old tests (`QuestionCount = 0`)
keep `MaxScore` as the denominator. Migration SQL: `UPDATE "TestResults" SET "Kind" = 'interim' WHERE "Kind" = '' OR "Kind" IS NULL;`

**`CenterMeta` (partial)**: `RatingTestWeight` int (default 60, 0..100), `RatingTopBottomCount` int (default 5,
1..10), `RatingTopBottomScope` string(8) (`group` default · `centre`).

### P4.2 Test entry (U4a)

- Create/update (admin `TestResultsController`, teacher `TeacherPortalController` existing endpoints): request gains
  `kind` (required on create: «Test turini tanlang») and `questionCount` (1..200, required on create). Update may
  lower `questionCount` only if no score exceeds it (400 «{n} ta o'quvchining natijasi yangi savollar sonidan katta»).
- List endpoints gain `month` (`yyyy-MM`) and `kind` filters: `GET api/admin/test-results?groupId=&month=&kind=`,
  `GET api/admin/test-results/groups?month=` (adds `monthTestCount`, `monthAvgPercent`), teacher `GET
  api/teacher/test-results?groupId=&month=&kind=`. DTOs gain `kind`, `questionCount`, `avgPercent`, `resultsSentAt`.
- New centre overview: `GET api/admin/test-results/overview?month=&kind=&courseId=&teacherId=` →
  `[{ testId, name, kind, date, groupId, groupName, teacherName, questionCount, scored, members, avgPercent }]`.
- Batch score save (partial `TestResultsController.Grid.cs`; teacher `TeacherPortalController.Tests.cs` with
  `OwnsGroup`): `PUT …/test-results/{id}/scores/batch` `{ rows: [{ studentId, solved: number | null }] }` →
  `{ detail: TestResultDetailDto, errors: [{ studentId, message }] }`. Valid rows are saved; invalid rows are NOT
  saved and NOT clamped («Natija 0 dan {n} gacha butun son bo'lsin», «O'quvchi bu guruh a'zosi emas»). `null` clears.
  The old single-cell `PUT {id}/scores` stays for compatibility.
- Detail rows gain `percent` (1 decimal) and `rank` (competition ranking by percent among scored rows).
- **Grid UI** (`pages/admin/tests/TestScoreGrid.tsx`, reused by `TeacherGroupTestsPanel`): rows sorted by student
  name and never re-sorted while typing; columns «№», «F.I.Sh», «Yechgan» (header «Ball» and range 0..MaxScore for
  old tests with `QuestionCount = 0`) (`NumberInput`, `inputMode` numeric,
  16 px on mobile), «Foiz» (live), «O'rin» (from last save); keys: Enter/↓ next row, Shift+Enter/↑ previous row;
  pasting multi-line text (Excel column) into a cell fills downward from that row (first column of each line,
  `"25/30"` → 25, empty clears); invalid cells red with tooltip; autosave = batch PUT 800 ms after the last edit and
  on blur; status «Saqlanmoqda…» / «Saqlandi» / red error (values kept).
- Test form (`TestFormModal` in `GroupTestsPanel.tsx`, `TeacherTestFormModal`): «Test nomi», «Sana», «Turi»
  (radio «Oylik» / «Oraliq», no default), «Savollar soni».
- Month filter («Oy» picker, default current month, option «Hammasi») on `TestResultsPage`, `TestGroupPage`, the
  group «Imtihonlar» tab and the teacher tests page; kind chips.
- **Send results**: `POST …/test-results/{id}/send-results` `{ resend?: boolean }` (admin create action; teacher
  `OwnsGroup`) → `{ queued, noTelegram, skippedUnchanged }`; 400 «"Test natijasi (ota-onaga)" avto-xabar qoidasi
  yoqilmagan — Xabarlar → Avto xabarlar» when no enabled `test_score` rule. For each scored, non-archived student:
  dispatch `test_score` with extra `{test}`, `{turi}`, `{sana}`, `{ball}` "25/30", `{foiz}` "83%", `{orin}` "3/18"
  (rank in this test / scored), `{guruh_orin}` and `{reyting_orin}` from the month's rating (`"—"` when unranked),
  `{guruh}`; `eventKey = "{testId}:{score}"` (unchanged scores are not resent; `resend=true` appends `:r{ticks}`),
  `sourceId = testId`. Sets `ResultsSentAt/ById`. Button «Ota-onalarga yuborish» on the test detail (admin + teacher),
  shows «Yuborilgan: 12.10 15:40».
- Audit (new) `TestResult` (section `classes`, EntityId `"{groupId}:{testId}"` so the group «Tarix» shows it):
  create, update, delete, results sent («Test natijalari ota-onalarga yuborildi: {test}, {n} ta»).
- Menu: «Testlar natijalari» moves from Future → O'quv bo'limi into the main «O'quv bo'limi» group.

### P4.3 Rating engine (U4b)

**Periods (`RatingPeriods.Resolve(kind, key, today)`, pure)**:
- `month`: key `yyyy-MM` (default current month).
- `quarter` («3 oy»): key = END month `yyyy-MM`; covers end−2 … end (rolling three calendar months).
- `year` («O'quv yili»): key = start year `yyyy`; 1 September … 31 August (default: today.Month ≥ 9 ? year : year−1).
- Ranges are clipped to today; future keys → 400 «Davr noto'g'ri». Labels: «Oktabr 2026», «Avgust – Oktabr 2026»,
  «2026/27 o'quv yili».

**Formula (`RatingFormula`, pure, `RatingFormulaTests`)** for period P:
- T(s,g) = mean of `percent` over tests of group g dated in P where s has a score (each test equal weight; missing
  score = not counted; percent clamped 0..100; tests with `MaxScore ≤ 0` ignored). Kind does not change the weight.
- Ball(s,g) = `StudentBallService` per group over the date range (new overload
  `ComputeByGroupAsync(db, groupIds, fromDate, toDate)`: journal `Date` between, criteria `Date` between,
  adjustments `CreatedAt` between from and to+`T23:59:59`).
- B(s,g) = Ball / max Ball in g × 100; absent when the group's max is 0.
- GroupScore = w·T + (1−w)·B with w = `RatingTestWeight`/100; only T present → T; only B → B; none → unranked.
  1 decimal.
- Group rank = competition ranking ("1, 2, 2, 4") by GroupScore among the group's ranked students.
- CentreScore(s) = mean of s's GroupScores; centre rank = competition ranking over all non-archived students with
  a CentreScore. Display order on ties: higher T, then name.
- Student set of group g in P = non-archived students with a test score or a journal ball in g within P
  (historical groups count for their period).

**`RatingEngine.ComputeAsync(db, period)`** (IO): SQL aggregates only — test scores joined to tests filtered by
date, `StudentBallService` grouped sums, students/groups name lookups; **never loads `JournalEntries` rows**.
Cached: `DataCache` key `rating:v2:{kind}:{key}`, deps `TestScore, TestResult, JournalEntry, CriterionGrade,
StudentBallAdjustment, StudentGroup, Student, Group, CenterMeta`, TTL 15 min.
Legacy `RatingService.SchoolAsync` (admin `GET students/rating`, portal `GET student/rating`) keeps its contract
AND its current numbers (ball, grade average, attendance = (conducted − non-late absences) / conducted — late is
already attended there) but is re-implemented with the SQL aggregates of MCP `TestTools.ComputeCenterAsync`; a
test proves both return identical rows on the same data. The new UI no longer calls these two endpoints.

API — new `RatingController` `[Route("api/admin/rating")]`; new page key `classes.rating` («Reyting»):
- `GET centre?period=&key=&q=&page=&pageSize=` `[AdminPerm("classes.rating", ReadRequiresPerm = true)]` →
  `{ period: PeriodDto, total, items: [{ rank, studentId, fullName, score, testAvg, testCount, ball,
  groups: [{ id, name, rank, score }] }] }`.
- `GET groups/{groupId}?period=&key=` `[AdminPerm("classes.rating", "classes.list", ReadRequiresPerm = true)]` →
  `{ period, group: { id, name, courseName, teacherName }, items: [{ rank, studentId, fullName, score, testAvg,
  testCount, ball, ballNorm }] }`.
- `GET students/{studentId}?period=&key=` `[AdminPerm("students.list", "classes.rating", ReadRequiresPerm = true)]`
  → `StudentRatingBlockDto`.
- `GET top-bottom?month=&scope=` (`classes.rating`) → preview (P4.5 shape).
- `GET/PUT settings` (`classes.rating`; PUT = edit) `{ testWeight, topBottomCount, topBottomScope }`; audit
  `SettingsAudit` («Reyting sozlamalari», withValues).
`PeriodDto = { kind, key, label, from, to }`.
`StudentRatingBlockDto = { studentId, period, centre: { rank | null, total, score | null, prevRank | null },
groups: [{ groupId, groupName, courseName, rank | null, total, score | null, testAvg | null, testCount, ball,
ballNorm | null }], components: { testAvg | null, testCount, ball, testWeight }, history: [{ month, centreRank | null,
centreTotal, score | null, testAvg | null }] (last 12 months, oldest first), tests: [{ testId, name, kind, date,
groupName, solved, questions, percent, rankInTest, takers }] (tests in the period, newest first),
attendance: { held, attended, percent | null } (via `AttendanceRules` for the period) }`.
`prevRank` = centre rank in the previous period of the same kind.
Portal: partial `StudentPortalController.Rating.cs`: `GET api/student/rating/summary?studentId=&period=&key=` →
`StudentRatingBlockDto & { groupTop: [{ groupId, groupName, rows: [{ rank, fullName, score, isMe }] }],
centreTop: [{ rank, fullName, score, isMe }] }` (top 10 + the student's own row when outside the top 10).
**No debtor lock** — balance is never checked.

### P4.4 Rating UI (U4c)

- Shared presentational component (`components/rating/StudentRatingBlock.tsx`) — "rich but uncluttered":
  1. period switch «Oy» · «3 oy» · «O'quv yili» + month/year picker;
  2. three tiles: «Markazda: 12-o'rin / 430» (arrow ▲3/▼2 vs previous period), «Guruhda: 3-o'rin / 18» (one chip
     per group if several), «Reyting bali: 84.6» with tooltip «Testlar 88% × 60% + Ball 79 × 40%»;
  3. line chart (12 months): centre rank (inverted axis) and score — two separate small charts, never a dual
     y-axis (`course-analytics.md` §6); single colour `#6366f1`;
  4. «Testlarim» table: date, name, kind badge, «25/30», «83%», «3/18»;
  5. attendance line «Davomat: 92% (46/50)».
- Admin student profile: new tab «Reyting» (after «Baholar va davomat») using the block.
- Parent/student cabinet `/student/progress`: tabs «Guruh» and «Markaz» are replaced by «Reyting» (the block in the
  cabinet's mobile style + group and centre top lists with «Siz» highlight); «Dastur» stays.
- Report page `/admin/hisobotlar/reyting` (`pages/admin/reports/RatingReportPage.tsx`, `RequirePerm perm="classes.rating"`):
  tabs «Markaz» (table + search) and «Guruhlar» (group select), period switch, CSV-free; menu: main «Hisobotlar»
  group («Reyting»), `config/reports.ts` entry `{ label: 'Reyting', to: '/admin/hisobotlar/reyting', perm:
  'classes.rating', inNav: true, description: "O'quvchilar test natijalari va ball bo'yicha qaysi o'rinda (oy,
  3 oy, o'quv yili)?" }` and route added to `mainReportRoutes`.
- Group page «Reyting» tab: new top section «Umumiy reyting (testlar + ball)» (group endpoint, period switch);
  the existing ball table stays below as «Ball tafsiloti».
- Settings: Sozlamalar → O'quv → new item «Reyting» (rows: «Reyting sozlamalari» → `RatingSettingsModal`: «Testlar
  ulushi, %» (0–100; «Ball ulushi» shown as 100 − x), «TOP/BOTTOM soni», «Qamrov» «Har guruh» / «Butun markaz»;
  `perm: 'classes.rating', action: 'edit'`). Place this item FIRST in the O'quv items (conflict avoidance, §8).

### P4.5 Monthly TOP / BOTTOM (U4d)

`TopBottomRules.Pick(students with T for the previous calendar month, N, scope)` (pure):
- scope `group`: per group with ≥ 2 scored students: k = min(N, ⌊n/2⌋); TOP = first k by T desc (ties by name),
  BOTTOM = last k (lowest T); scope `centre`: one list of students' centre T (mean of their group T) with the same k.
- A student is never in both lists; students without any test in the month are not considered.
`MonthlyRatingNotifier` (hosted, every 10 min): for each enabled `rating_top`/`rating_bottom` rule that
`AutoMessageSchedule.IsDue` and has no marker `rating:{ruleId}:{yyyy-MM}` (rated month = previous calendar
month): build the lists and dispatch rule by rule (`DispatchStudentAsync(…, onlyRuleId: rule.Id)`; `rating_top`
rules → TOP students, `rating_bottom` rules → BOTTOM students; per-student claim `rating:{ruleId}:{yyyy-MM}:{studentId}`)
with extra `{oy}` = rated month name, `{orin}` "r/n" (rank by T / number of scored students in the scope), `{foiz}`
T (e.g. "78%"), `{guruh}` (group name, or «markaz» for centre scope), `eventKey = "{yyyy-MM}"`; mark the rule done
for that month when no unclaimed failure remains. Like every monthly reminder (`CustomReminderService`), there is no
catch-up: if the server is down for the whole scheduled day, that month's TOP/BOTTOM is skipped (logged). Preview endpoint
`GET api/admin/rating/top-bottom?month=&scope=` → `{ month, scope, count, groups: [{ groupId, groupName, top:
[{ studentId, fullName, testAvg, rank }], bottom: [...] }] }` (centre scope: one pseudo-group `id = ""`, «Markaz»),
shown on the report page tab «TOP/BOTTOM».

### P4.6 MCP impact (U4m — list)

`tests_list` (kind, questions, month filter), `test_scores` (solved/percent/rank), `student_tests`, `rating_group`
and `rating_center` (composite score with `period`/`key`; ball and test average as components), new
`rating_top_bottom` (preview, read-only).

### P4.7 Tests

- `RatingPeriodsTests`: month/quarter/year ranges, academic year boundary (Aug 31 / Sep 1), clipping, invalid keys.
- `RatingFormulaTests`: weights 60/40; only tests; only ball; group max 0; competition ranking with ties; centre =
  mean of group scores; equal-weight tests; archived excluded.
- `RatingEngineTests` (SQLite): engine equals formula on seeded data; query count does not grow with journal rows
  (EF command interceptor counts ≤ fixed N); legacy `SchoolAsync` equals MCP `ComputeCenterAsync`.
- `TestScoreBatchTests`: out of range rejected (no clamp); non-member rejected; null clears; percent/rank; legacy
  decimal tests still accept 0..MaxScore integers; questionCount lowering guard.
- `TestScoreMessagesTests`: tokens; unchanged score not resent; resend; rule disabled → 400.
- `TopBottomRulesTests`: k = min(N, ⌊n/2⌋); no overlap; scope centre; ties.
- `MonthlyRatingNotifierTests`: due on day 3 at 10:00 once; restart no duplicate.
- RBAC: `classes.rating`; portal summary only for own child.
- Client: `ratingPeriods.test.ts`; `TestScoreGrid` paste parsing (`parsePastedColumn`) and key navigation.

### P4.8 Risks

Centre ranking across different courses compares different tests (accepted by the client; components are shown
for transparency); heavy history (12 cached monthly computations — warm cache < 200 ms, cold < 2 s target);
teachers entering scores before tests are finished (manual send only).

### P4.9 Acceptance criteria

1. A test «Unit 5», 12.10.2026, «Oraliq», 30 questions: pasting an 18-line Excel column fills 18 rows; «Foiz» shows
   solved/30×100; a value 31 is red and not saved; Enter/↑/↓ move between rows.
2. Month filter shows only that month's tests.
3. Month rating = 0.6·T + 0.4·B with B relative to the group's best ball; tied scores share a rank.
4. Profile «Reyting»: period switch, three tiles, 12-month charts, «Testlarim»; the cabinet shows the same for the
   own child with group/centre top lists; a debtor sees everything.
5. «Ota-onalarga yuborish» sends {ball}, {foiz}, {orin}, {reyting_orin}; sending again sends only changed scores.
6. On the rule day/time (default 3rd, 10:00) TOP/BOTTOM messages go once per student for the previous month,
   k = min(5, ⌊n/2⌋) per group.
7. No rating endpoint loads whole journal tables (engine test).

---

## 6. Phase 5 — Dashboard, schedule, tasks

### P5.1 Schedule (U5a)

**`Room` (partial)**: `Floor` int? (−2..50; null = «Qavat ko'rsatilmagan»). `CreateRoomRequest` + `int? Floor`;
`UpdateRoomRequest` + `int? Floor` (null = untouched) + `bool ClearFloor = false`; `RoomDto` + `Floor`. Rooms page
form field «Qavat». Audit `Room` includes the floor change.

**Dated schedule** — `ScheduleController`: `GET api/admin/schedule/dated?from=&to=` (`schedule.timetable`; default
today; `to − from ≤ 6 days`, else 400) → `{ from, to, rooms: [{ id, name, floor, capacity }], teachers: [{ id, name }],
lessons: [{ date, groupId, groupName, courseName, teacherId, teacherName, substituteTeacherId, substituteTeacherName,
roomId, roomName, floor, start, end, moved, members, capacity, attendanceTaken, status }], skippedGroups }` — lessons
from `AttendanceWatch.LessonsOn` per day (non-archived groups, moves, substitutes, group lifetime), members =
`MembershipLifecycle.SeatHolders`. `GET export` gains `date=yyyy-MM-dd` (dated day; `day` kept).

**UI** (`/admin/jadval` and the home schedule block): view switch «Kun» / «Hafta»; date navigation ‹ «Bugun» ›
(week = Monday–Sunday of the selected date); «Xona bo'yicha» / «O'qituvchi bo'yicha»; filters «O'qituvchi»,
«Guruh», «Kurs», «Xona», «Qavat», «Holat». Day view (`DatedDayBoard`): time axis × columns; room columns grouped
under floor headers («1-qavat», «2-qavat», «Qavat ko'rsatilmagan»), floors ascending, rooms by natural name. Week
view (`WeekBoard`): rows = rooms (grouped by floor) or teachers, columns = 7 dates, cells = lesson chips «14:00
Group A» sorted by time; moved lessons marked «ko'chirilgan», substitutes «o'rinbosar: …». URL params `date`,
`view`, `groupBy` (replace, not push). The old weekday template tabs are removed from both pages.

**Hard block of double booking** — `ScheduleConflicts` (pure, replaces `RoomConflictService` logic; the service
becomes a thin IO wrapper): two groups conflict when both are non-archived, have days and valid times
(`ScheduleRules.MakeSlot`), share a weekday, their half-open time intervals overlap, their date ranges
`[StartDate or −∞, EndDate or +∞]` overlap, and they share the room (`RoomId`) or the teacher (`TeacherId`).
- `ClassesController.Create`, `Update` (only when RoomId, TeacherId, Days, StartTime, EndTime, StartDate or EndDate
  changed) and `Unarchive`: conflict → **409** `{ roomConflict: true, message: "Jadvalda to'qnashuv bor —
  saqlanmadi", conflicts: RoomConflictDto[], canForce }`. `force=true&forceReason=...` is honoured ONLY for
  superadmin (`User.IsInRole(Roles.SuperAdmin)`) with a non-empty reason → saved + audit `Group` «Jadval
  to'qnashuvi bilan saqlandi: {conflicts} — sabab: {reason}». `canForce` = caller is superadmin. Others: no override.
- `JournalService.RescheduleLessonAsync`: the target date/time (given time or the group start; same duration)
  is checked against every lesson on that date (`AttendanceWatch.LessonsOn`) for the same room or the same
  teacher (owner or that day's substitute) → `InvalidOperationException` «Bu vaqtda xona/o'qituvchi band: {guruh}
  {HH:mm–HH:mm}». `RescheduleLessonRequest` + `Force`, `ForceReason`: only the admin journal endpoint and only a
  superadmin may force (audit as above); the teacher endpoint never forces.
- Substitute assignments keep the existing warning-only behaviour; `CompleteAndTransfer` (superadmin-only) excludes
  the closed group from the check and is not blocked.
- `GET api/admin/schedule/conflicts` (`schedule.timetable`) → `[{ kind: 'room'|'teacher', groupA: { id, name },
  groupB: { id, name }, days: number[], slot }]`; the schedule page shows a banner «Jadvalda {n} ta to'qnashuv bor»
  with the list (existing data may already conflict).
- Client: group form/pages catch 409 → conflict modal; superadmin sees «Baribir saqlash» with a required «Sabab» field.

### P5.2 Tasks — approval step (U5b)

**`WorkTask` (partial)**: `ApprovalRequired` bool (default false — existing tasks untouched), `ApprovedAt`
(DateTime?), `ApprovedById` (string, `""`), `ReturnCount` int, `LastReturnedAt` (DateTime?).
Rules (`WorkTaskApproval`, pure):
- Create: `ApprovalRequired = input.approvalRequired ?? (assignee != "" && assignee != creator)`.
- Moving into a done column (panel or bot «✅ Bajardim») keeps today's behaviour (CompletedAt, repeat spawn) and, if
  `ApprovalRequired`, adds event `submitted` «Bajarildi — tasdiqlash kutilmoqda» and notifies the creator
  (Telegram via the creator's `TelegramRegistration.UserId` + bell): «✅ {mas'ul} topshiriqni bajardi: «{title}».
  Tasdiqlang.» with inline button «✅ Tasdiqlash» (`callback_data = "wtok:{taskId}"`). This lives in ONE method
  `WorkTaskFlow.OnCompletedAsync(db, task, actorId)` called by the panel's `ApplyMoveAsync`, by
  `SyncCompletionAsync` and by the bot's `HandleWorkTaskDoneAsync` (same "single place" rule as `SpawnRepeatAsync`);
  the Telegram text/keyboard live in `WorkTaskTelegram`. No notification when the creator is the one completing it.
- `PUT api/admin/work-tasks/{id}/approve` (`tasks.board`, edit): task in a done column, `ApprovalRequired`,
  `ApprovedAt == null`; caller = creator or admin/superadmin, else 403 «Faqat topshiriq bergan xodim tasdiqlaydi».
  Sets `ApprovedAt/ById`, event `approved`, Telegram to the assignee «👍 Topshiriq tasdiqlandi: «{title}»».
- `PUT api/admin/work-tasks/{id}/return` `{ reason }` (1..500, same gate): moves to the board's first non-done
  column (order 0), `CompletedAt = null`, `ApprovedAt = null`, `ReturnCount++`, `LastReturnedAt`, event `returned`
  «Qaytarildi: {sabab}», Telegram to the assignee «↩️ Topshiriq qaytarildi: «{title}» — {sabab}».
- Leaving a done column by any other move clears `ApprovedAt`. Both actions run under `WorkTaskFlow.LockKey`.
- Bot `wtok:{taskId}`: sender chat must map to the creator's or an admin/superadmin's account; otherwise toast
  «Bu amal sizga ruxsat etilmagan».
- DTO gains `approvalRequired, approvedAt, approvedByName, returnCount, state` with `state` ∈ `open` «Ochiq»,
  `overdue` «Kechikkan», `awaiting_approval` «Tasdiq kutmoqda», `approved` «Tasdiqlangan», `done` «Bajarildi».
  List `status` accepts `awaiting`, `approved`; `assigneeId=me` and new `createdById=me` resolve to the caller.
  Dashboard rows gain `awaitingApproval`, `approved`, `returned`.
- UI: `TaskModal` checkbox «Bajarilgach tasdiqlash talab qilinsin»; card badges; buttons «Tasdiqlash» /
  «Qaytarish» (reason prompt) for the allowed users; dashboard columns.
- New boards' default columns: «Rejada», «Jarayonda», «Bajarildi» («Tekshiruvda» no longer seeded; existing
  boards untouched).
- Menu: «Topshiriqlar» moves from Future → Boshqaruv into the main «Boshqaruv» group (after «Rollar»).

### P5.3 Home widgets (U5c)

**`UserSettings` (partial)**: `HomeLayout` text (default `""`).
`HomeLayout` JSON: `{ version: 1, cardsVisible: boolean, cards: [{ key, visible }], blocks: [{ key, visible }] }`.
Cards = the 12 `HOME_CARDS` keys. Blocks: `cashboxes` (superadmin only), `schedule` (`schedule.timetable`),
`lessonsMonitor` (`classes.notAttended`), `myTasks` (`tasks.board`), `absentNotices` (`students.notices`),
`redList` (`students.attendance`), `contactsToday` (`contacts`), `aiAnalysis` (`ai`).
Defaults: cards all visible in `HOME_CARDS` order, `cardsVisible = true`; blocks `cashboxes` ✓, `schedule` ✓,
others hidden. `HomeLayoutRules.Normalize` (pure): version must be 1 (else 400), unknown keys dropped, duplicates
dropped, missing known keys appended with their default visibility, JSON ≤ 4 000 chars.
API — new `HomeLayoutController` `[Route("api/admin/home/layout")]`, `[Authorize(Roles = "superadmin,admin,staff")]`
(caller's own row only): `GET` → `{ layout: HomeLayout | null }`; `PUT` body `HomeLayout` → normalized layout;
`DELETE` → 204 (reset).
UI: `AdminDashboard` renders cards and blocks in layout order, filtered by permission; the «Statistika» button
toggles `cardsVisible` (saved to the server; the old `localStorage['home.showStats']` is read once as the initial
value when the server has no layout); new «Vidjetlar» button → `HomeWidgetsDrawer` (checkbox + ▲/▼ per item, two
sections «Kartochkalar» / «Bloklar», «Standartga qaytarish»). Block components: schedule (dated day view),
`TodayLessonsMonitor` (compact), «Mening topshiriqlarim» (open + awaiting-my-approval counts and top 5),
«Davomat xabarlari» (today's counts with link), «Qizil ro'yxat» (top 10), «Bog'lanish kerak — bugun» (contacts
meta `todo` count), `CenterAiAnalysisCard`, `CashboxBalanceStrip`. Catalog in `lib/homeWidgets.ts` (no components
in that file — react-refresh rule).

### P5.4 MCP impact (U5m — list)

`schedule_day` (dated, floor, substitutes), `rooms_list` (floor), `tasks_list` (approval state, return count), new
`schedule_conflicts`.

### P5.5 Tests

`ScheduleConflictsTests` (half-open touch is not a conflict; date ranges; archived ignored; room vs teacher),
`ClassesConflictTests` (409 for admin; superadmin force requires reason; unchanged schedule fields → no check;
unarchive), `RescheduleConflictTests`, `DatedScheduleTests` (moved lesson on its new date/time; substitute; group
lifetime), `RoomFloorTests` (update without floor keeps it; ClearFloor), `WorkTaskApprovalTests` (default
requirement; approve gate 403; return moves + clears; reopen clears approval; bot callback gate; repeat spawn not
duplicated), `HomeLayoutRulesTests` (normalize, unknown keys, version), RBAC for every new endpoint. Client:
`homeWidgets.test.ts` (defaults, permission filter), schedule date helpers, nav tests (new file per phase, §8).

### P5.6 Risks

Existing conflicting groups (report + no check on unrelated edits); staff used to `force` (now superadmin only);
the home page is the edutizim-parity screen (defaults keep it identical).

### P5.7 Acceptance criteria

1. «Vidjetlar»: hide, show and reorder cards and blocks; the layout follows the user to another browser.
2. «Kun» view for a chosen date shows real lessons (moved lessons on their new date/time) with rooms grouped by
   floor; «Hafta» shows Monday–Sunday; teacher filter works in both.
3. Saving a group whose room or teacher overlaps another active group's slot is refused (409) for everyone except
   a superadmin who gives a reason (audited).
4. Moving a lesson into an occupied room/teacher slot is refused with the conflicting group named.
5. A task assigned to another user, marked done by the assignee, shows «Tasdiq kutmoqda»; the creator approves
   (panel or Telegram) → «Tasdiqlangan», or returns it with a reason → back to the first column and the assignee
   is notified; self-assigned tasks have no approval step. «Topshiriqlar» is in Boshqaruv.

---

## 7. Phase 6 — Accounts

### P6.1 Permanently visible student passwords (U6a)

**Store**: `StudentCredential` (new, table `StudentCredentials`): `UserId` string(36) PK (student's `AppUser.Id`),
`PasswordCipher` bytea, `Source` string(16) (`create` · `reset` · `admin_set` · `self_change` · `backfill`),
`UpdatedAt` DateTime. Never in backup (`ExcludedTables` += `"studentCredentials"`; `SecretScrub` already drops
`…Password…`/`byte[]`), never in any MCP `Reads`, never in logs or audit.

**`CredentialVault`** (Application, `FaceVault` clone): AES-256-GCM, blob `[1 byte version=1][12 nonce][16 tag]
[ciphertext UTF-8]`; key `.env` `CREDENTIAL_VAULT_KEY` (base64, 32 bytes) → compose
`Credentials__VaultKey: "${CREDENTIAL_VAULT_KEY:-}"` → `AppSecrets.CredentialVaultKey`
(`Read("Credentials:VaultKey", EnvKeys.CredentialVaultKey)`); add to `.env.example` and `EnvKeysWiringTests`.
Missing/invalid key → `Configured = false`: nothing is stored (never plaintext), startup warning, superadmin banner
«Parollarni saqlash o'chiq: .env da CREDENTIAL_VAULT_KEY yo'q».

**Capture — no call site can forget it**: `AppUser` (partial) gains `[NotMapped] PendingPasswordChange?
PendingPassword` (`record PendingPasswordChange(string Kind /* set|clear */, string? Plain, string Source)`).
`AccountExtensions.SetInitialPassword(this AppUser, string plain, string source = "admin_set")`,
`SetOwnPassword` (`self_change`) and `BlockLogin` (`clear`) set it; `AccountFactory.CreateAccountFor` calls
`SetInitialPassword(plain, "create")` instead of writing the two fields. Source mapping at call sites: account
creation (student create, import, lead conversion, credentials auto-create) → `create`; every «reset password»
endpoint and the group `ensure` → `reset`; the edit form's new password and `Restore` with `NewPassword` →
`admin_set`. `StudentCredentialInterceptor` (Infrastructure `SaveChangesInterceptor`, registered in
`AddCrmAccounts` as `services.AddSingleton<IInterceptor, StudentCredentialInterceptor>()` — §0.2 step 5): in
`SavingChanges[Async]` → `DetectChanges()`, for every Added/Modified `AppUser` with `PendingPassword != null` and
`Role == "student"`: `set` + vault configured → upsert the row; `clear` → delete the row; then null the property.
Same `SaveChanges`, same transaction. `InitialPassword` behaviour (cleared on login, bot login message) is unchanged.
Startup backfill `student-credential-backfill-2026-10` (only when configured; otherwise retried next start): every
student account with `InitialPassword != null` and no row → `backfill` row.
`StudentPurge` deletes the row with the account. Cabinet «Parolni o'zgartirish» screen gets the line «Eslatma:
markaz administratorlari yangi parolingizni ko'ra oladi».

**Reveal** (`StudentCredentials.Reveal(row, user, vault)`): row decrypts → `(password, known: true, source,
updatedAt)`; else `InitialPassword` → `(…, known: true, source: "initial")`; else `(null, known: false)`.

**Gate** (unchanged single gate, `auth-sessions.md` §3): `AdminPermAttribute.HasFullAccess(User, "students.list")`
(admin/superadmin always). Every response carrying a password sets `Cache-Control: no-store`.

API:
- `GET api/admin/students/{id}/credentials` (existing) → `CredentialsDto` + `known`, `source`, `updatedAt`,
  `vaultConfigured` (append). `GET students/export` and `POST students/export-selected` use Reveal (export-selected
  shows passwords to everyone passing `HasFullAccess`, not only superadmin).
- New `GroupCredentialsController` `[Route("api/admin/classes/{groupId}/credentials")]`,
  `[AdminPerm("students.list", ReadRequiresPerm = true)]` + `HasFullAccess` check (403 «Login/parolni ko'rish uchun
  «O'quvchilar» bo'limiga to'liq ruxsat kerak»):
  - `GET` → `{ vaultConfigured, rows: [{ studentId, fullName, status, login, password, known, telegramChats }] }`
    (live, non-archived members; no account → `login: ""`; never creates anything).
  - `POST ensure` `{ onlyUnknown: true }` → creates missing accounts and regenerates passwords of `known=false`
    rows (`SetInitialPassword(new, "reset")` + `RefreshTokenService.RevokeAllForUserAsync`), per-student audit
    (existing «O'quvchi paroli qayta yaratildi», `StudentDiscount` type) + one `Group` row; returns the GET shape
    + `{ created, regenerated }`.
  - `GET export` → xlsx «№, F.I.SH., Login, Parol, Telegram, Holat», file `login_parollar_{guruh}_{sana}.xlsx`;
    audit `Group` «Login/parollar eksport qilindi: {guruh} ({n} ta o'quvchi)» (no passwords in the row).
- UI: profile card section «Kirish ma'lumotlari» (Login + copy; Parol masked with 👁 + copy; «Yangi parol»;
  unknown → «Parol saqlanmagan — «Yangi parol» bilan yarating»), visible only when the client mirror of the gate
  (`canStudentCreds`: admin/superadmin or full `students`/`students.list`) passes. Group page «O'quvchilar» tab:
  button «Login/parollar» → `GroupCredentialsModal` (table with copy buttons, «Excel yuklab olish»,
  «Paroli noma'lumlarga yangi parol yaratish ({n})» with confirm «Ularning eski parollari ishlamay qoladi»).
  Passwords are never part of any list endpoint payload.

### P6.2 "Telegram ulangan / ulanmagan" column (U6b, after U2a)

- `Student` (partial) `[NotMapped]`: `TelegramLinked` (int — usable chats: `BlockedAt == null`),
  `TelegramRelations` (List<string>, distinct relations of usable chats), `TelegramBlocked` (int).
  `StudentListView.EnrichExtrasAsync` fills them with one grouped query.
- Teacher views: `TeacherStudentRowDto` and `TeacherGroupStudentDto` + `TelegramLinked`, `TelegramRelations`
  (counts and relations only — no phones, names or chat ids). Admin group page: `GroupMemberDto`
  (`GET api/admin/classes/{id}/members`) + the same two fields (append, defaults 0 / empty).
- UI: `ActiveStudentsPage` and `StudentsPage` («Faol» tab) column «Telegram»: relation icons (👨 Otasi, 👩 Onasi,
  👪 Ota-onasi, 🧑 O'zi, ❔ Boshqa) + count, red badge «Ulanmagan» when 0, grey «bloklagan» counter; filter chip
  «Telegram: Hammasi / Ulangan / Ulanmagan» (client-side). Same column in the teacher portal «O'quvchilarim» and
  the group «O'quvchilar» tab (teacher + admin).

### P6.3 MCP impact (U6m — list)

`students_search` / `student_profile` (Telegram linked count and relations). Credentials: never exposed (add
`StudentCredentials` to the self-test's forbidden-table check in `McpReadOnlyRole.SelfTest`).

### P6.4 Tests

`CredentialVaultTests` (round trip, wrong key → null, not configured → nothing stored), `StudentCredentialInterceptorTests`
(create/reset/self-change/block in the same SaveChanges; non-student ignored; key missing → no row — `TestDb`
gets an overload that adds interceptors, since tests build the context without DI),
`StudentCredentialBackfillTests`, `GroupCredentialsTests` (403 without full access; ensure regenerates only unknown
and revokes sessions; export audit has no password; `no-store` header), `BackupExcludesCredentialsTests`,
`EnvKeysWiringTests` update, `TelegramLinkSummaryTests` (blocked excluded from linked), RBAC tests.

### P6.5 Risks

Privacy expectation of students who change their own password (cabinet notice); key loss → passwords unreadable
(not lost logins: reset per group); a `dpkeys`-style volume is NOT used precisely because it is outside backups.

### P6.6 Acceptance criteria

1. With the key set, a student's password stays visible to admins after the student's first login and after the
   student changes it in the cabinet (new password shown).
2. Group «Login/parollar» lists every member; Excel export works and is audited; «Paroli noma'lumlarga yangi parol»
   regenerates only unknown ones.
3. A staff user without full `students.list` gets 403; the MCP role cannot read `StudentCredentials`; the JSON
   backup has no such table; no log line contains a password.
4. Active students list, the teacher's «O'quvchilarim» and the group students tab show Telegram count + relation
   icons; «Ulanmagan» filter works.

---

## 8. Modules, parallelisation and shared files

### 8.1 Units

| Unit | Main files (new unless marked ✎ edit) | Depends on | Parallel? |
|---|---|---|---|
| U0 | ✎ `Entities.cs`, ✎ `Entities.AttendanceNotify.cs`, ✎ `AppDbContext.cs`, ✎ `IAppDbContext.cs`, ✎ 4 controllers (partial), `Server/Crm/CrmSetup.cs`, ✎ `Program.cs` | Phase 1 | no — first, alone |
| U2a | `Domain/Crm/Messaging.cs`, `AppDbContext.CrmMessaging.cs`, `Services/Messaging/*`, ✎ `TelegramService.cs`, ✎ `TelegramBotService.cs`, `MessagingController.cs`, ✎ `MessagesController.cs`, ✎ `BackupService.cs`, ✎ `Program.cs` (private folder), ✎ `AuditSections.cs` | U0 | yes (wave 1) |
| U2b | ✎ `AutoMessageService.cs`, ✎ `AutoMessageTriggers.cs`, ✎ `MessageTokenCatalog.cs`, ✎ `MessageTokenizer.cs`, ✎ `PaymentReminderService.cs`, `DebtMessages.cs`, ✎ `BirthdaySmsService.cs`, ✎ `CustomReminderService.cs`, ✎ `AutoMessagesController.cs` | U2a | same agent as U2a |
| U2c | `StudentAchievementsController.cs`, `StudentPortalController.Achievements.cs`, `AchievementService.cs`, ✎ `StudentPurge.cs` | U2a, U2b | same agent |
| U2d | client `pages/admin/messages/*`, `components/messaging/*`, `lib/telegramHtml.ts`, `lib/relations.ts`, `api/services/messaging.ts`, `achievements.ts`, profile sections, ✎ `StudentDetailPage.tsx`, ✎ `pages/student/Certificates.tsx` | U2a–U2c API | same agent |
| U3a | `Services/Attendance/AttendanceRules.cs`, ✎ `StudentJournalBuilder.cs` + listed call sites, ✎ `JournalService.cs` (SetEntry/Bulk), ✎ `LessonAttendanceReminderService.cs`, ✎ `DashboardController.cs`, ✎ `SettingsController.cs` (absence reasons), ✎ `CatalogGuards.cs`, `Domain/Crm/Attendance.cs`, `AttendanceSettingsController.cs`, client `classes/detail/shared.ts`, reasons UI, settings modals | U0 | yes (wave 1) |
| U3b | ✎ `AttendanceNotify.cs`, ✎ `AttendanceNotifyWorker.cs`, ✎ `JournalController.cs`, ✎ `TeacherPortalController.cs` (journal PUT/bulk only), `AttendanceNoticesController.cs`, `AbsentNoticesPage.tsx` | U3a, U2a | wave 2 |
| U3c | `AttendanceDisciplineService.cs`, `AttendanceFinesController.cs`, `TeacherPortalController.Fines.cs`, ✎ `SalaryLedger.cs`, ✎ salary DTOs, ✎ `PenaltyPage.tsx`, teacher salary UI | U3a | wave 2 (same agent as U3b) |
| U3d | `AbsenceStreaks.cs`, `AbsenceStreakService.cs`, `AttendanceRedListController.cs`, `StudentsController.Absences.cs`, ✎ `KpiMetricsService.cs`, ✎ `StudentAbsencePage.tsx`, profile section, ✎ `navigation.ts` (Bog'lanish kerak) | U3a | wave 2 (same agent) |
| U4b | `Services/Rating/*`, `RatingController.cs`, `StudentPortalController.Rating.cs`, ✎ `StudentBallService.cs`, ✎ `RatingService.cs`, `Domain/Crm/Rating.cs` | U0 | yes (wave 1) |
| U4c | `components/rating/*`, `RatingReportPage.tsx`, profile «Reyting» tab, ✎ `pages/student/Progress.tsx`, ✎ group «Reyting» tab, ✎ `config/reports.ts`, ✎ `navigation.ts`, settings modal | U4b | wave 1–2 (same agent) |
| U4a | ✎ `TestResultService.cs`, ✎ `TestResultsController.cs`, `TestResultsController.Grid.cs`, `TeacherPortalController.Tests.cs`, ✎ `TeacherPortalController.cs` (test create/update only), test UI, `TestScoreMessages.cs`, ✎ `navigation.ts` (Testlar natijalari), ✎ `AuditSections.cs` | U2b | wave 2 |
| U4d | `MonthlyRatingNotifier.cs`, `TopBottomRules.cs`, report tab | U4b, U2b | wave 2 (same agent) |
| U5a | `ScheduleConflicts.cs`, ✎ `RoomConflictService.cs`, ✎ `ClassesController.cs` (Create/Update/Unarchive), ✎ `JournalService.RescheduleLessonAsync`, ✎ `ScheduleController.cs`, ✎ `RoomsController.cs`, `Domain/Crm/Workspace.cs`, schedule UI, ✎ `AdminDashboard.tsx` (schedule block only) | U0 | yes (wave 1) |
| U5b | `WorkTasksController.Approval.cs`, ✎ `WorkTasksController*.cs`, ✎ `WorkTaskFlow.cs`, ✎ `WorkTaskTelegram.cs`, ✎ `TelegramBotService.cs` (one `else if`), tasks UI, ✎ `navigation.ts` | U0 | yes (wave 1, same agent as U5a) |
| U5c | `HomeLayoutController.cs`, `lib/homeWidgets.ts`, `HomeWidgetsDrawer.tsx`, ✎ `AdminDashboard.tsx` | U3b, U3d, U5a | wave 3 |
| U6a | `Services/Accounts/*`, `StudentCredentialInterceptor.cs`, ✎ `AccountExtensions.cs`, ✎ `AccountFactory.cs`, ✎ `StudentsController.cs` (credentials/export), `GroupCredentialsController.cs`, `Domain/Crm/Accounts.cs`, ✎ `BackupService.cs`, ✎ `.env.example`, ✎ `docker-compose.yml`, profile card, group modal | U0 | yes (wave 1) |
| U6b | ✎ `StudentListView.cs`, ✎ `TeacherHomeService.cs`, ✎ `TeacherPortalController.Students.cs`, ✎ `ClassesController.cs` (`members` endpoint only), list columns, teacher pages | U2a | wave 2 (same agent as U6a) |
| U2m…U6m | `Application/Mcp/Tools/*`, ✎ `McpToolRegistry.cs`, `WunderkindLC.Tests/Mcp/*` | the phase's units | sequential, one agent, after each phase merges |

### 8.2 Waves (agents)

- **Wave 0:** U0 (one agent, merge immediately).
- **Wave 1 (parallel):** Agent A = U2a→U2b→U2c→U2d · Agent B = U3a · Agent C = U4b→U4c · Agent D = U5a→U5b ·
  Agent E = U6a. Merge order: A, B, C, D, E (each later merge rebases and resolves the additive conflicts below).
- **Wave 2 (after A and B merged):** Agent B = U3b→U3c→U3d · Agent C = U4a→U4d · Agent E = U6b.
  Merge order: B, C, E.
- **Wave 3:** Agent D = U5c.
- MCP units: one dedicated agent, sequential; each U<N>m starts as soon as its phase is merged.
- Migrations are generated by the integrator after each merge (§0.3), in merge order. A unit whose branch is
  older than the last merged migration rebases first; it never edits another phase's migration.

### 8.3 Shared files — sequential edits (merge conflicts expected, all additive)

| File | Who | How to keep it mergeable |
|---|---|---|
| `Infrastructure/Migrations/*`, `AppDbContextModelSnapshot.cs` | integrator only | never in build branches |
| `Domain/Entities.cs`, `AppDbContext.cs`, `IAppDbContext.cs` | U0 only | phases use partial files (§0.2) |
| `Server/Program.cs` | U0, U2a (private folder line) | everything else via `Crm<Name>Setup.cs` |
| `Application/Services/AuditSections.cs` | U2a, U3b, U3c, U4a | each adds its own lines in its section block |
| `Application/Services/BackupService.cs` | U2a, U3c, U3d, U6a | one line each in `ExcludedTables` / one `Add` call each |
| `Application/Services/TelegramBotService.cs` | U2a, U5b | U5b adds only its `else if (data.StartsWith("wtok:"…))` branch after the `wtdone:` branch |
| `Application/Services/JournalService.cs` | U3a (SetEntry/Bulk), U5a (Reschedule) | different methods |
| `Server/Controllers/TeacherPortalController.cs` | U3b (journal handlers), U4a (test create/update) | different regions; new endpoints only in new partial files |
| `Server/Controllers/StudentsController.cs` | U6a (credentials/export methods) | U2/U3 use new partial files |
| `Client/src/App.tsx` | U3b, U3d, U4c, U5a/U5b, U6a | add each route next to its related routes (`nazorat/…`, `students/davomat`, `hisobotlar/…`, `jadval`, `topshiriqlar`) — different hunks; routes MUST stay in `App.tsx` (tests parse it) |
| `Client/src/config/navigation.ts` | U3b (Nazorat), U3d (O'quvchilar + Future), U4a (O'quv bo'limi + Future), U4c (Hisobotlar), U5b (Boshqaruv + Future) | each touches its own group and its own Future sub-group |
| `Client/src/config/constants.ts` (`adminPermissions`) | U3b (`students.notices`), U4b (`classes.rating`), U2d (label) | different sections |
| `Client/src/config/settingsGroups.ts`, `pages/admin/settings/SettingsGroupPage.tsx` | U3a (2 rows + 2 modal keys), U4c (item «Reyting» FIRST in O'quv + 1 modal key) | separate positions; union-type line conflicts resolved by keeping both |
| `Client/src/config/reports.ts` | U4c | — |
| `Client/src/pages/admin/students/StudentDetailPage.tsx` | U2d («Yutuqlar» tab after «Sertifikatlar», Aloqa section), U3d (section at top of «Baholar va davomat»), U4c («Reyting» tab after «Baholar va davomat»), U6a (card section) | content in separate component files; `Tab` union additions resolved by keeping both |
| `Client/src/pages/admin/AdminDashboard.tsx` | U5a (schedule), U5c (widgets) | same agent, sequential |
| `Client/src/types/index.ts` | U6b (Student fields), others only if needed | append-only |
| Nav/menu tests | U3d, U4a, U4c, U5b | do NOT edit `reports.test.ts` arrays; add `config/__tests__/navCrmPhase<N>.test.ts` asserting each moved item is in its main group and absent from Future (update an existing assertion only if it actually breaks) |
| `McpToolRegistry.cs` | MCP units | one agent |

---

## 9. Definition of Done (every unit)

- [ ] Backend tests green: `docker run --rm -v "$PWD":/src -v wlc-nuget:/root/.nuget/packages -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test WunderkindLC.Tests/WunderkindLC.Tests.csproj`
      (incl. `PermissionCatalogTests`, `AuditSectionsTests`, `McpReadOnlyTests`, `EnvKeysWiringTests`).
- [ ] Client: `npx tsc -b --noEmit`, `npx vitest run`, `npx eslint` and `npm run build` pass.
- [ ] Every new endpoint is RBAC-protected and has an RBAC test (403 for a staff user without the key).
- [ ] Each phase's migration exists after merge, contains no DROP of a table/column, `database update` works on a
      fresh DB and on a prod-schema restore; data SQL from the phase block applied; startup data fixes idempotent.
- [ ] New tables are in `BackupService` or `ExcludedTables`; new audit types mapped in `AuditSections`.
- [ ] No new direct parent/student Telegram send outside `TelegramOutbox`.
- [ ] Every write in the client shows server errors (`apiErrorMessage`), modals keep input.
- [ ] Acceptance criteria of the phase verified manually on a dev bot (`DEV_` tokens), screenshots in the PR.
- [ ] `.claude/rules/` updated: `messaging.md` (outbox, relations, ack, schedule), `journal.md` (one definition,
      AttendanceTakenAt, notices), `tests.md` (kind, grid, send results), new `rating.md`, `schedule.md` (dated view,
      floors, hard block), `topshiriqlar.md` (approval, menu), `auth-sessions.md` (credential vault), `contacts.md`
      (automatic streak requests), `billing.md` (attendance fines).
- [ ] Existing functionality still works (journal, payments, salary, contacts, tasks, leads).

---

## 10. Open questions (recommended decision stands if the owner stays silent)

| # | Question | Recommended decision (in force) |
|---|---|---|
| 1 | Fine plus the existing «Maoshni jurnalga bog'lash» deduction for the same never-marked lesson? | Both apply; they are independent (discipline vs unpaid lesson). |
| 2 | TOP/BOTTOM per group or centre? | Per group, N = 5, k = min(N, ⌊n/2⌋); switchable in «Reyting sozlamalari». |
| 3 | Rating weights | Tests 60 %, ball 40 %; editable. |
| 4 | Should «keldi» notices also wait until the lesson end? | No — immediate. |
| 5 | Should teachers see student passwords? | No. |
| 6 | Do old online-test scores count in the rating? | Yes (valid scores). |
| 7 | Holidays | No holiday calendar; bulk-waive fines by date. |
| 8 | Attachment and delivery retention | Local files 60 days (Telegram `file_id` reused after), delivery log 180 days. |
| 9 | Achievements on the public landing page? | No. |
| 10 | Parent (role `parent`) account passwords in the vault? | No — students only (owner's wording). |
