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
