# Assumptions log

- [2026-09-10] Repo reused for a NEW project; must have zero ties to the previous owner's
  infrastructure → created a fresh root `.env` with every integration key blank (blank = module
  disabled, per `.env.example` and `docker-compose.dev.yml`) → the code already fails closed on
  empty credentials, so no code changes were needed to disconnect it.
- [2026-09-10] `WunderkindLC.Client/.env` is TRACKED in git and contained the previous project's
  live PostHog token → blanked `VITE_POSTHOG_KEY`/`VITE_POSTHOG_HOST` → in dev, Vite reads that
  file, so page views and `posthog.identify` user ids would have been sent to the previous
  owner's analytics account.
- [2026-09-10] Same token was a hardcoded fallback in `Dockerfile` (ARG default) and
  `docker-compose.yml` (`${VITE_POSTHOG_KEY:-phc_...}`) → emptied both defaults → an empty
  `.env` value would otherwise be silently overridden at image build time.
- [2026-09-10] Host ports 5432/8080/5173 are taken by the user's other projects → local run uses
  the free block 7700-7702 via `docker-compose.override.yml` (gitignored) → `ports` needs the
  `!override` tag because Compose MERGES port lists instead of replacing them.
- [2026-09-10] `dev.sh` used explicit `-f` files, so Compose did NOT auto-load
  `docker-compose.override.yml` → added an optional third `-f` when that file exists → keeps
  `./dev.sh up` as the single documented entry point.
- [2026-09-10] Ran the DEV compose (hot reload, separate Vite container) rather than the prod
  image → the goal is further development on a new project, not a production deploy; nginx,
  cloudflared, mediamtx and backup stay off locally.
- [2026-09-10] Project renamed IntellectCRM -> WunderkindLC (namespaces, projects, folders, root
  directory, docker names) -> the school gets a SEPARATE project, so an LC-specific name is
  correct here; a neutral name would only have mattered if one codebase served both.
- [2026-09-10] The code is SINGLE-TENANT ("Bitta markaz — bitta CenterMeta qatori"; no
  TenantId/CenterId in the domain; `Branch` is only a geo-fence for attendance) -> a second
  organization needs a SECOND deployment, not a second subdomain on this one.
- [2026-09-10] Old owner's real contacts were hardcoded as DEFAULTS, not just samples:
  `CenterMeta` property initializers (Entities.cs), `LandingCmsController` `Default*` constants
  (used as public read-time fallbacks), `PublicCertificatesPage` useState defaults, and static
  landing/certificate pages incl. Yandex/Google map links to the old address -> all blanked or
  neutralized -> a new install with an empty CMS would otherwise have PUBLISHED another
  business's phone, Instagram, email and street address.
- [2026-09-10] Added a guard in `PublicLandingLeadTests` asserting no old-brand literal survives
  in `LandingCmsController` -> the previous test only checked the write path, so read-time
  defaults slipped through.
- [2026-09-10] Uzbek word "intellekt" (as in "sun'iy intellekt" = AI) is spelled with a K and was
  deliberately EXCLUDED from the rename -> replacing it would have corrupted AI panel copy.
- [2026-09-10] Dev client container bumped node:20-slim -> node:22-slim in the local override:
  `undici@8`/`jsdom` in package.json require Node >=22 and vitest's jsdom environment crashed with
  "webidl.util.markAsUncloneable is not a function" -> pre-existing, unrelated to the rename; the
  prod Dockerfile (node:20-alpine) is untouched because the build never runs those test-only deps.
- [2026-09-10] Demo data (10 per section) was created through the REAL admin API, not by SQL
  inserts -> validation, audit rows, membership lifecycle and the billing engine all ran, so the
  data is internally consistent; direct inserts would have produced groups with no charges and an
  empty audit trail.
- [2026-09-10] Memberships are ACTIVATED from the 1st of the current month (not left as `trial`)
  -> `ChargeActivationProrateAsync` then wrote 10 MonthlyCharges (5 250 000) -> without this,
  Finance, Journal, Salary and Course-analytics would all render empty and the demo would look
  broken rather than new.
- [2026-09-10] Finance `Category` must be the KEY from `constants.ts`
  (income: tuition|donation|rent_in|other, expense: salary|utilities|supplies|rent|repair|other),
  not an Uzbek label -> the first pass used labels, which made `tuitionIncome` read 0 because
  `FinanceController` matches `Category == "tuition"` literally; rows were deleted and recreated.
- [2026-09-11] Which parts of the WunderkindLMS design to port -> ported the design TOKENS
  (brand ramp -> LMS blue #3366ff, Inter font, page background #f6f7fb, softer radii/shadows),
  the app shell (Sidebar/Topbar), the shared UI primitives (Button/Card/PageHeader/Modal/StatCard)
  and the full Leads board; every other page follows automatically because it consumes
  `brand-*` and those primitives -> the user asked for the "general design style", and a
  page-by-page rewrite of 50+ screens carries regression risk with no extra visual gain.
- [2026-09-11] Lead-card aging colours vs the LMS clean-white card -> the card is now always
  WHITE (LMS style); the age is shown by the top-right chip, and the coloured LEFT EDGE is
  drawn only for leads that need attention (>=7 days old, or converted) -> tinting every card
  made a wall of coloured cards in which the "urgent" signal no longer stood out, but dropping
  the signal entirely would lose why the aging colours were added in the first place.
- [2026-09-11] Kanban column background -> set by the COMPONENT via `stageColors[...].tint`,
  not by `.kanban-col` in index.css -> unlayered CSS beats Tailwind utilities, so a background
  declared in the class would override `bg-blue-50` and every column would look the same grey.
  Same change applied to the contacts and tasks boards, so all three boards stay identical.
- [2026-09-18] Menu items edutizim has but we don't (Gamifikatsiya, Onlayn kurs, P&L …) → not shown at all → client: "umuman kerak emas"; a link to an empty page would look broken to staff.
- [2026-09-18] Where do our teachers go in the edutizim menu? → Boshqaruv → Xodimlar opens our teachers list; staff accounts stay under Rollar → edutizim keeps teachers inside Xodimlar; a unified Xodimlar page is phase 5.
- [2026-09-18] edutizim branch selector → shown as a static label with our center name → we have no branch switching; a fake dropdown would mislead.
- [2026-09-18] edutizim top-bar extras (language, dark mode, Yangiliklar, Qanday ishlaydi?, subscription pill, TEXNIK YORDAM, robot, lightning FAB) → not copied → vendor features we don't have; "Tezkor bo'limlar" kept with the same two items.
- [2026-09-18] Which edutizim sections are cloned page-by-page? → only Lidlar, Guruh, O'quvchilar, Moliya (+ group page, student profile, payments, schedule, home); other groups keep the edutizim menu place but open our existing pages unchanged → client: "eng asosiy narsalar … juda ortiqcha narsalar kerakmas".
- [2026-09-18] Home "Buyurtmalar" — what is an open lead? → every lead with `ConvertedStudentId == null` → the model has no "lost" stage: a rejected lead is DELETED (snapshot in `ArchivedRecords`, type `lead`), so whatever is left unconverted is open.
- [2026-09-18] Home "Birinchi darsga keladiganlar" — which field? → distinct open leads with a `TrialLesson.Result == "pending"` scheduled TODAY or later → a pending trial whose date has passed is a stale record, not someone who "will come"; counting it would inflate the card month after month.
- [2026-09-18] Home "Buyurtmadan ketganlar" → leads deleted this month (`ArchivedRecords` type `lead`, `DeletedAt` in the current month) → same source `CenterAiAnalysisService` already uses for "students who left this month".
- [2026-09-18] Home "Yangi/Aktiv o'quvchidan ketganlar" → (student, course) intervals from `CourseAnalytics.MergeIntervals` that CLOSED this month and are not "completed"; "aktiv" if any membership in the interval was ever activated (`MembershipLifecycle.FirstActivatedAt`, past periods included), else "yangi"; a course-less group counts as its own course → a group switch or a certificate must not look like churn (course-analytics.md §1).
- [2026-09-18] Home "Muzlatilgan" → students whose overall `MemberState` is frozen/yearFrozen (no live active/trial membership), non-archived → differs from the old header (any frozen membership): a student frozen in an old group but studying in a new one is active, not frozen (MemberState invariant).
- [2026-09-18] Home "Yangi o'quvchilar" / "Aktiv o'quvchilar" / "Qarzdorlar" → kept the old dashboard definitions (any live trial / any live active membership / balance < 0; non-archived students) → task said "same as existing"; a student active in one course and on trial in another appears in both cards.
- [2026-09-18] Home "Birinchi to'lovni qilganlar" → students whose earliest `income/tuition` transaction date falls in the current month (refunds ignored) → same convention as `LeadOutcome.FirstPaidAt`.
- [2026-09-18] Lesson block bottom row "done/total" (edutizim "7/52") → done = distinct conducted `LessonNote` (date, period); total = lesson days between the group's StartDate and EndDate; when either date is missing the block shows seat-occupying members/capacity with a users icon instead of a book → we have no per-group lesson plan total; showing "7/0" or a guessed number would be wrong.
- [2026-09-18] Home day param and teacher columns → `?day=` uses JS numbering (0=Yak, Sunday-first like edutizim's tabs), the API keeps 0=Mon; teacher mode lists ALL non-archived teachers (edutizim shows idle teachers too) → the URL maps 1:1 to the tab order; conversion lives only in `lib/scheduleGrid.ts`.
- [2026-09-18] Home "Filtr" has no time-range control → `fromHour`/`toHour` stay URL-only (default 08:00–22:00, grid auto-extends to fit lessons outside it) → edutizim's filter row has exactly five selects; the time range lives in its URL too. "Holati" = group status (Faol/To'lgan/Bloklangan); the tooltip's "Daraja" row is omitted (no level on groups).
- [2026-09-18] Lidlar table "ID" column → first 6 hex chars of the lead GUID (also searchable) → we have no sequential lead number like edutizim's "3446".
- [2026-09-18] Lidlar table O'QITUVCHI / KURS / KURS DARAJASI / MODERATOR → teacher of the lead's trial-lesson group (pending first lesson, else latest trial) / `Lead.InterestSubject` / latest level-test result (`LevelTestSubmission.Level`) / `Lead.AssigneeUserId` (plain text) → leads have no teacher or level field of their own and we have no staff profile page to link to.
- [2026-09-18] Buyurtmalar filters "Ichki manba", "Qaysi filialdan/ga o'tkazilgan", "Kun", "So'rovnoma", "Kategoriya" and the customize-filter / filter-appearance buttons → not shown; "Status" = converted or not; our Tuman/Maktab filters kept → no data behind them; every filter we had before stays.
- [2026-09-18] «Birinchi darsga yozilganlar» rows → same definition as the home card (open lead + trial with result "pending"); past-dated pending trials stay in the list tinted #FFCACA; with several pending trials the nearest upcoming one wins, else the latest past one; sorted by first-lesson date ascending → one definition (`LeadFirstLesson`, test locks card count == non-red rows), and an unmarked past trial is exactly what the manager must revisit.
- [2026-09-18] «Birinchi darsga yozilganlar» filters "Kun" / "Toq/Juft kunlar" / "Ranglar bo'yicha" → weekday of the first-lesson date / trial group's schedule entirely on Du-Chor-Ju or Se-Pay-Sha / past (red) vs upcoming → edutizim's exact semantics are not visible from the UI.
- [2026-09-18] Routes `/admin/leads/birinchi-dars` and `/admin/leads/:id` and `GET api/admin/leads/first-lesson` use the existing `leads.list` key, no new permission key → they show the same leads as the list (permissions.md §10 asks for a key per routed page, but a separate key here would let a staff member see a subset of data they are otherwise denied).
- [2026-09-18] Lead page (edutizim `/orders/info/:id`) → "Mas'ul shaxs" and "O'qituvchi" read-only, "Referal bergan o'quvchi" and "Transfer" not built, "Guruhga qo'shish" = our "O'quvchiga aylantirish" / "Sinov darsiga yozish" in the ⋮ menu, extra right tab "Sinov darslari" → no reassign API, teacher comes from the trial group, no referral data, no branches; the old LeadDetailModal was removed once nothing used it.
- [2026-09-18] Kanban card right-footer hint → trial status ("Sinov: dd.mm HH:mm" / red if past / "Sinov yo'q" / "O'quvchi") instead of edutizim's "Topshiriq yo'q"; column mini-map not built; drag keeps no confirmation dialog → leads have no tasks; there was no drag confirmation before and crm-leads.md requires none.
- [2026-09-18] edutizim "Moliya → Kassalar" (cashbox balances) → our Moliya menu points to /admin/finance?tab=cashiers; our payment-taking page /admin/kassa moved to Future → we have no cashbox entity; the closest existing view is the cashier breakdown, and the payment form is edutizim's Kirim drawer, not a menu item.
- [2026-09-18] Lead list showed raw GUIDs in KURS and MANBA → the server now resolves a course/source id to its NAME in the leads list DTO (same rule as the first-lesson list) → old/imported rows store ids while the form stores names; a GUID in the table looks broken to staff.
- [2026-09-18] Student profile left card → replaced with the edutizim card (avatar, phone+copy, SMS/Balans/Call buttons, stat rows); group and teacher names dropped from that card → edutizim's card has no such rows and both are visible in "Guruh"/"Shaxsiy ma'lumotlar".
- [2026-09-18] Group page default tab → "O'quvchilar" (was "Jurnal") → edutizim opens the student roster first; our other tabs keep their order after it.
- [2026-09-18] Moliya → Tranzaksiya turi → read-only catalog of our fixed categories → category codes drive money logic (tuition/refund/salary); letting staff rename or delete them would silently break billing, salary % and cashier reports.
- [2026-09-18] Moliya → Shartnoma → points to our existing contracts page (same as O'quv bo'limi → Shartnoma) → edutizim has a separate payment-plan contract; we have no such entity and building one was not requested.
- [2026-09-18] Pul oqimi → only "Operatsion faoliyat" is shown (no investing/financing sections) → our transactions carry no such classification; an always-empty section would only mislead.
- [2026-09-18] Moliya analitikasi → one centre instead of edutizim's branch list → we run a single centre; the left card shows the centre's own running balance.
- [2026-09-18] Jarima → stored as an ordinary expense transaction with category "penalty" (no new table) → it then flows into kassa, kirim-chiqim and P&L like any other expense.
- [2026-09-18] O'quvchilar → Ota-ona → a student list with father/mother name+phone+balance; our app-parent accounts page moved to Future → that is what edutizim's page shows; our old page is about the parent MOBILE APP (device, last login), a different question.
- [2026-09-18] Students list keeps our columns and gains edutizim's missing ones (To'lov sanasi, Yaratilgan sanasi, Manba, Moderator) instead of dropping Guruh/Holat/Jinsi → staff use those daily and edutizim simply lacks them.
