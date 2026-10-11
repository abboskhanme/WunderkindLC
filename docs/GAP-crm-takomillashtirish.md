# CRM upgrade — gap analysis (client meeting script, 2026-10-11)

Source: transcript of the client meeting (5 sections). Every item in the script is a required
change. This file records what the code does TODAY (read on 2026-10-11, commit `70a0f07a`) so the
SPEC can be written against facts. Status: `EXISTS` / `PARTIAL` / `MISSING`.

Standing fact from the user: the centre uses NO installable mobile app — web + Telegram (Mini App)
only. Push notifications and group chats are leftovers and must be removed.

## 1. Notifications (Telegram)

| Requirement | Status | Today |
|---|---|---|
| Template tokens | EXISTS | `MessageTokenizer`, `MessageTokenCatalog`; rendered only at send time |
| Preview before send | MISSING | `MessageEditor.tsx` is a textarea + chips; no preview endpoint |
| Bold / italic / link | MISSING | `TelegramService` accepts `parseMode`, no messaging caller passes it; token values are not escaped |
| Photo / video / file | MISSING | `SendBroadcastRequest` has no file field; no `sendVideo`; `SendPhotoReturningIdAsync`, `SendDocumentReturningIdAsync` exist |
| Absent notice at lesson END | PARTIAL | `attendance_absent` fires IMMEDIATELY on marking (`AttendanceNotify`, `AttendanceNotifyWorker`); no send-after field; 6 h expiry |
| Debt reminder | PARTIAL | `PaymentReminderService`: hardcoded 09:00 on ODD days of month, rule schedule fields ignored; one message per course; manual send with debtor filter exists (`SmsModal`, composer `OnlyDebtors`) |
| Thank-you on bot connect ("01") | MISSING | `TelegramBotService.CompleteAsync` sends hardcoded text + login + APK; no trigger |
| Birthday | EXISTS | `BirthdaySmsService` (students only) |
| Test results to parents (score, rank) | MISSING | `SetScoreAsync` only saves; `test_result` trigger is the LEAD level test |
| Top / Bottom 5 monthly | MISSING | no monthly aggregation of tests |
| Achievements (IELTS / CEFR / certificate) | MISSING | only `LandingCertificate` (landing CMS, free-text name, no StudentId) |
| Messaging archived students | MISSING | bulk paths filter `!IsArchived`; archive-tab "SMS yuborish" returns 400 (bug) |
| "Understood" button + status in CRM | MISSING | no per-recipient Telegram log (`Broadcast` is aggregate only; auto-message Telegram sends are not logged); confirm exists only for push (`UserNotification.ConfirmedAt`) |
| Recipient count before send | MISSING | shown only after send; Telegram count = `TelegramRegistration` rows |

Telegram callback prefixes already in use: `check_sub*`, `support`, `ot: ocode omb omt oa: og: ofin
ocon oedit ocan olist`, `kb: kq: kqm kpc kpk kconf krcp klist kcan`, `wtdone:`.

## 2. Attendance

| Requirement | Status | Today |
|---|---|---|
| Teacher must mark during lesson | PARTIAL | `JournalPolicy` edit modes (`today` / `window`); no "during lesson" window |
| Repeated reminders | PARTIAL | `LessonAttendanceReminderService`: once per rule (`LessonReminderMark`); repeat only via several rules; no escalation to admin |
| Fine for unmarked lesson | PARTIAL | `SalaryRequireJournal` + `SalaryGraceDays`: proportional deduction `contribution × missed / planned` (`SalaryLedger`); no fixed fine; "Jarima" page lists manual expenses only |
| Manager hold / confirm of absent notices | MISSING | no status, controller or UI over `AttendanceNotifyItems` |
| 3 / 5 consecutive unexcused → red list + contact task | PARTIAL | `KpiMetricsService.AbsenceStreaksAsync` (KPI retention role only; counts late/excused/grade-only entries, mixes groups); nothing creates a `ContactRequest` |
| Excused vs unexcused | MISSING | `AbsenceReason` = `Name, Short, IsLate`; "unexcused" is `Name.Contains("sababsiz")` |
| Profile: missed days + reasons | PARTIAL | percent, monthly chart, reason chips, journal modal; no flat list of missed dates |

Note: "attendance taken" = `LessonNote.Conducted` (also set by a grade); the stricter
`LessonNote.AttendanceTaken` is not read by reminder / deduction / monitor.
No attendance marked → nothing is sent to parents (confirmed).

## 3. Students and bot accounts

| Requirement | Status | Today |
|---|---|---|
| 2–3 Telegram accounts per student | PARTIAL | unique `(StudentId, ChatId)` allows it, but registration matches ONE phone field (`ParentPhone`, or `Phone` by `TelegramPhoneMatchField`); `FatherPhone` / `MotherPhone` not matched; no relation field, no blocked flag |
| "All parents" = connected + active only | PARTIAL | sends to registrations; no count shown, bot-blocked chats not tracked |
| Login / password easy to see | PARTIAL | shown right after creation and in the edit form; `InitialPassword` cleared on first login; Excel export superadmin-only, no per-group export |
| Active vs archive strictly separate | PARTIAL | Faol / Arxiv tabs exist; bulk messaging to archive impossible |

## 4. Tests and grading

| Requirement | Status | Today |
|---|---|---|
| Interim test any time, max score | EXISTS | `TestResult` (name, date, max score) from admin and teacher portal |
| Test kind (monthly / interim) | MISSING | no kind field |
| Excel-like entry | PARTIAL | one input per student, save on blur, rows re-sort on each save; no Enter / arrow / paste |
| Month filter | MISSING | lists are per group, date desc |
| Rating in profile and parent cabinet | PARTIAL | cabinet `/student/progress`: group + centre TOP 15, based on journal BALL (not tests); admin profile has no group / centre position |
| Rating locked for debtors | MISSING | no balance check in `StudentPortalController` |

## 5. Dashboard, schedule, tasks

| Requirement | Status | Today |
|---|---|---|
| Configurable widgets | PARTIAL | `home.showStats` toggles the whole card block (localStorage); 12 cards in `lib/homeCards.ts` |
| Daily + weekly schedule | PARTIAL | `/admin/jadval`: weekday tabs (template, not dates); weekly grid only on Future page |
| Teacher filter | EXISTS | `HomeFilterRow` |
| Rooms and floors | PARTIAL | columns by room; `Room` has free-text `Building`, no `Floor` |
| No double booking | PARTIAL | `RoomConflictService` (room OR teacher) only WARNS; `?force=true` saves anyway |
| Tasks: assign, deadline, done / approved | PARTIAL | module exists under Future → Boshqaruv; no approval step ("Tekshiruvda" is an ordinary column); daily digest reminder |

## 6. Removal footprint (push, group chat, APK)

Push-only (removable): `FcmService.SendAsync` + `MutedTokensAsync`, `DeviceToken`,
`UserSettings.NotificationsEnabled`, `AutoMessageRule.SendPush`, `MessagesController` `push/*`,
`settings/firebase`, `/api/public/push-config`, `notifications/register` (student, teacher),
client `push.ts`, `webpush.ts`, push channel in composer / auto rules / history,
`FirebaseSettings.tsx`, student "Push bildirishnoma" toggle.

Group chat (removable): `ChatService`, `ChatHub` (`/hubs/chat`), `ChatMessage`, `MessagesController`
`last-messages` + `chat/{className}`, teacher and student chat endpoints and pages, `ChatPanel`,
`unread-context`, nav entries and badges, `TeacherPermissions.Messages`.

APK: `settings/app-apk`, `CenterMeta.Student/TeacherApk*`, bot sends APK after registration,
`ApkSettings.tsx`, "Ilova" nav group (`ParentsPage`, `TeacherAppPage` device columns).

MUST STAY (shared):
1. `FcmService.SendDataAsync`, `FCM_SERVICE_ACCOUNT_JSON`, `CtiAgent.FcmToken` — CTI dial and
   Local SMS wake, Play Integrity fallback (`AppAttestation`).
2. `NotificationStore`, `UserNotification`, notification endpoints and bells — used by the WEB
   cabinets. Writes are currently inside `if (rule.SendPush)` → decouple before removing the flag.
3. `GET api/admin/messages/classes` — under the chat gate but feeds the composer.
4. SignalR itself — shared with `LiveHub` (turnstile).
5. `firebase-messaging-sw.js` — also the PWA installability worker.
6. `UserSettings` (language / theme), `PersonalizePush*` helpers (reused by SMS),
   group-name uniqueness rule.
7. Support Telegram (`BotSupportController`) — independent of group chat.

Tests touching push / chat: `ChatAccessTests`, `AuditFixT6Tests`, `AutoMessageTests` (asserts via
`SendPush` + `UserNotifications`), `AuditFixFx4Tests`, `AuditFixT1Tests`, `AuditFixT7Tests`,
`AuditFixT10Tests`, `StudentMembershipViewTests`, `StudentPurgeTests`, client `settingsGroups.test.ts`.

Tables are NOT dropped in the removal phase (code only) — dropping is a separate, confirmed step.

## Open questions

Answered by the user on 2026-10-11 — see the `[2026-10-11]` entries in `docs/ASSUMPTIONS.md`.
Scope grew: Local Call / Local SMS (CTI) and the whole SMS channel are removed too, the online mode of
group tests is removed, and a read-only MCP server is the first deliverable (`docs/SPEC-mcp.md`).

## Found while building MCP tools (2026-10-11)

- Attendance "late" is counted inconsistently: the daily attendance page (`StudentAttendanceController.Absent`)
  counts a late student as PRESENT, while the student-app / admin journal summary (`StudentJournalBuilder`
  `Attended`) counts only explicit "present" marks or graded lessons — an ungraded late lesson is not
  "attended" there. Fix in the attendance phase (one definition: late = attended).
- Default "present" also differs inside the panel: the group page client (`ClassDetailPage` /
  `GroupAttendanceTab`, `autoPresent`) treats a CONDUCTED lesson with no mark as present, while
  `StudentJournalBuilder` / journal.md use ATTENDANCE TAKEN. MCP follows journal.md. Unify in the attendance phase.
- **FIXED 2026-10-11 — debt reminder amount overstated.** `PaymentReminderService.BuildMessagesAsync` summed
  `StudentGroupLedger` months including the 3 ADVANCE months shown for active memberships (full fee each):
  parents received "debt" = real debt + up to 3 × monthly fee, and a subject with no debt still produced a
  "debt" message. Now only months ≤ current month count. Regression test:
  `AutoMessageTests.Qarzdorlik_summasiga_AVANS_oylari_QOSHILMAYDI_va_qarzsiz_fanga_xabar_ketmaydi`.
- `RatingService.SchoolAsync` (admin "rating" endpoint) loads the WHOLE `JournalEntries` table into memory
  (~21M rows in prod per code comments). The MCP `rating_center` tool computes the same numbers with SQL
  aggregates (test proves equality) — the panel endpoint should be moved to the same approach (performance phase).
- `JournalPolicy.GetAsync` now reads only the policy columns of `CenterMeta` (same result) — required for the MCP
  read-only role's column grant; also a smaller query for every journal request.
