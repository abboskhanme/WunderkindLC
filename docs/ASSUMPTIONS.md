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
- [2026-09-19] edutizimdagi "Tranzaksiya turi" erkin tahrirlanadi, bizda esa toifa KODI butun
  moliya mantig'ini boshqaradi (tuition/salary/penalty) → har tur `BaseCategory` orqali mavjud
  kodga bog'landi (`ContactStage.BaseStatus` naqshi) → erkin nom ham bo'ladi, hisob-kitob ham
  buzilmaydi
- [2026-09-19] Tur bo'yicha pul yo'nalishi ALOHIDA so'ralmaydi (bo'limdan kelib chiqadi:
  vaucher→kirim, jarima→chiqim) → aks holda "chiqim bo'limidagi kirim turi" kabi zid yozuv
  paydo bo'lardi
- [2026-09-19] Turlar seed'i FAQAT jadval bo'sh bo'lganda yoziladi (tizim ustunlaridan farqli)
  → foydalanuvchi o'chirgan tur har restartda qayta tug'ilmasin
- [2026-09-25] Xodim roldan ruxsatni NUSXA qilib oladimi yoki jonli bog'lanadimi? → jonli
  (`AppUser.RoleTemplateId`; rol o'zgarsa hammasi yangilanadi) → foydalanuvchi "ruxsat rolga
  beriladi" dedi; nusxada rolni tahrirlash mavjud xodimlarga ta'sir qilmay chalg'itardi
- [2026-09-25] Eski xodimlar rolga avtomatik biriktirilmaydi → "Individual ruxsatlar" bo'lib
  qoladi, admin tahrirda rol tanlaydi → ruxsatlarini taxmin bilan o'zgartirib yubormaslik uchun
- [2026-09-25] Foiz bazasi o'zgarishi kuchga kirish oyi `2026-09` (`CenterMeta.SalaryChargedBaseFrom`,
  migratsiya qo'yadi) → foydalanuvchi
  "joriy oydan" dedi, o'tgan oylar maoshi o'zgarmasin
- [2026-09-25] Hisoblangan bazada VOZVRAT va 100% CHEGIRMA o'qituvchi foizini kamaytiradimi? → yo'q
  (baza = `MonthlyCharge.Amount`) → foydalanuvchi "to'liq oylikdan, chegirmani markaz ko'taradi"
  dedi; muzlatish vozvratida oylikning o'zi qisqargani uchun natija avvalgidek. Tasdiq so'raldi
- [2026-09-27] O'qituvchi portali "tizim bilan bir xil" — qaysi tizim? → admin paneli (Nunito, brand
  #3d68ff, #f0f2f2 fon, oq kartalar) → teal/Times New Roman ko'rinish eski sahifalarda ham token
  darajasida (`.teacher-app`) almashtirildi, har faylni qayta yozmasdan
- [2026-09-27] "O'quvchilarim" da pul qancha ko'rinsin? → balans SUMMASI yo'q, faqat "to'lov
  kutilmoqda (N oy)" belgisi → jurnal balansni ko'rsatadi, lekin umumiy ro'yxatda ortiqcha pul
  ma'lumoti kerak emas; telefon raqamlari ham berilmadi (aloqa — "Bog'lanish kerak" navbati orqali)
- [2026-09-27] Dars jadvali qaysi ruxsat bilan? → mavjud `schedule` ("Dars jadvali") kaliti;
  o'quvchilar ro'yxati → `journal` → yangi ruxsat kaliti qo'shilmadi; bosh sahifa ruxsatsiz, lekin
  jadval ruxsati bo'lmasa bugungi darslar bo'sh keladi (403 emas)
- [2026-09-27] Tezkor davomatda belgilanmagan o'quvchi qanday ko'rinadi? → BELGILANMAGAN (foydalanuvchi
  talabi) va hammasi belgilanmaguncha saqlab bo'lmaydi; saqlash tasdiqlash oynasi orqali → jurnal
  qoidasi yozuvsiz o'quvchini «keldi» deb o'qiydi, qisman saqlash yolg'on davomat berardi. FAQAT
  o'zgarganlar yuboriladi — har "kelmadi" ota-onaga avto-xabar, qayta saqlashda takrorlanmasin
- [2026-09-27] O'qituvchi portali botga qanday ulanadi? → «🎓 O'qituvchi paneli» web_app tugmasi
  (faqat o'qituvchi chatida) + oddiy login; Telegram orqali avtomatik kirish YO'Q → foydalanuvchi
  tanlovi: yangi kirish yo'li ochilmasin
- [2026-09-27] Telefon qaysi formatda? → `+998 90 123 45 67` (kiritish va ko'rsatish) → O'zbekistonda
  umumqabul qilingan; jadvaldagi qisqa `93 854 70 83` (PhoneChip) edutizim bilan bir xillik uchun qoldi
- [2026-09-27] Bo'sh summa maydoni saqlanganda nima bo'ladi? → majburiy maydonda xato matni, ixtiyoriyda 0
  → foydalanuvchi "0 ni o'chirib bo'sh qoldirish" ni so'radi; bo'sh ≠ 0 faqat UI'da, so'rovda eski qiymat
- [2026-09-27] Kasr son kiritiladimi? → YO'Q, hamma joyda butun son (foydalanuvchi qarori); ISTISNO —
  KPI formulasi koeffitsiyentlari (−0,2 · 1,2 · 35,99%) → ular formulaning o'zida kasr, butun qilinsa
  KPI hisobi buzilardi (forma ochilganda yaxlitlanib ketardi). Bazada boshqa kasrli qiymat deyarli yo'q
  (guruh narxi, oylik hisob, foizlar — hammasi butun; 4 ta tranzaksiya kasrli)
- [2026-09-27] edutizim "Funksionallik" qatorlari bizda nimani ochadi? → Moliya sozlamalari = to'lov
  nazorati, Davomat sozlamalari = jurnal tahrirlash siyosati, O'qituvchi holatlari = maosh va jurnal
  (uchalasi `JournalPolicyModal` ning o'z bo'limi), Xonalar holati = Xonalar sahifasi, Topshiriq
  boshqaruvi = Baholash mezonlari → mazmuni edutizimdagi izohga eng yaqin mavjud sozlama
- [2026-09-29] O'quvchi "sinfi" — guruhmi yoki maktab sinfimi? → MAKTAB sinfi (1–11 + «Maktabni bitirgan»),
  yangi `Student.SchoolGrade`/`SchoolGradeYear`; eski `ClassName` (asosiy guruh yorlig'i) tegilmadi → foydalanuvchi
  maqsadi "nechanchi sinflar o'qiyapti" statistikasi
- [2026-09-29] Sinf har yili qo'lda yangilanadimi? → YO'Q: kiritilgan o'quv yiliga bog'lab saqlanadi va har
  1-sentabrda o'zi oshadi (`SchoolGrades.Current`), 11 dan keyin «bitirgan» → aks holda statistika bir yilda eskirardi
- [2026-09-29] «Maktabgacha» varianti kerakmi? → qo'shilmadi (avto-oshish yoshga bog'liq emas, noto'g'ri 1-sinf
  chiqarardi); bunday o'quvchi «Sinfi kiritilmagan» da qoladi → oddiylik
- [2026-09-29] Statistika qayerda? → «Hisobotlar → Sinflar kesimi» (`/admin/hisobotlar/sinflar`, `students.list`,
  standart qamrov — AKTIV o'quvchilar) + o'quvchilar ro'yxatida «Sinf» filtri (sonlari bilan) → yangi endpoint
  kerak bo'lmadi, manba o'quvchilar ro'yxatining o'zi
- [2026-09-29] OSM xarita qatlami → global `Referrer-Policy: no-referrer` o'zgarmadi, faqat xarita rasmlariga
  `strict-origin-when-cross-origin` (`OsmTileLayer`) → `/uploads` manzillari tashqariga chiqmasin
- [2026-09-29] "Tuman va maktab umuman qolmasin" — bazadagi ustun/jadvallar ham o'chirilsinmi? → Hozircha
  YO'Q: kod, UI, API, filtrlar, lid formasi katalogi va Sozlamalar sahifasi olib tashlandi; `Students`/`Leads`
  dagi `DistrictId`/`SchoolId` va `Districts`/`Schools` jadvallari bazada qoldi → qaytarib bo'lmaydigan
  ma'lumot o'chirish alohida tasdiq talab qiladi. Markazning O'Z manzilidagi «Tuman» (`CenterMeta.District`) tegilmadi
- [2026-09-30] "O'quvchini butunlay o'chirish" — kim qila oladi? → foydalanuvchi: "role berilgan istalgan
  odam" → mavjud `students.list:delete` («O'quvchilar ro'yxati» → «O'chirish») kaliti, yangi kalit ochilmadi →
  Rollar matritsasida allaqachon bor va arxivdagi eski o'chirish ham shu kalitda
- [2026-09-30] Butunlay o'chirishda o'quvchining to'lovlari ham o'chsinmi? → YO'Q: tirik to'lovi bor o'quvchi
  rad etiladi (avval to'lovni bekor qilish kerak), bekor qilinganlari saqlanib o'quvchidan ajratiladi →
  moliya yozuvlari hech qachon o'chirilmaydi (billing.md), kassa hisoboti orqaga o'zgarmasin
- [2026-09-30] Butunlay o'chirishdan keyin tarixda iz qolsinmi? → o'quvchining barcha audit qatorlari o'chadi,
  faqat BITTA "kim, qachon, kimni o'chirdi" qatori qoladi → qaytarib bo'lmaydigan amalning javobgari ko'rinsin
- [2026-10-02] "...dan ketganlar" / "Birinchi to'lovni qilganlar" kartochkalari qayerga? → yangi ro'yxatlar
  `/admin/students/ketganlar` va `/admin/students/birinchi-tolov` (`GET students/month-movement`, ruxsat
  `students.list`, arxivlanganlar HAM) → ro'yxat kartochka sonini hisoblaydigan funksiyaning o'zidan, son teng
  chiqsin. "Birinchi to'lov" kartochkasi `finance.main` bilan ko'rinadi, lekin bosish `students.list` talab qiladi
- [2026-10-02] Bu ikki ro'yxat menyuga qo'shilsinmi? → yo'q, faqat «Hisobotlar» hub'ida (oy tanlab ko'rish uchun)
  → asosiy kirish kartochkadan, menyuni kengaytirish so'ralmagan
- [2026-10-01] "Muzlatilgan" kartochkasi oddiy + «aktiv muzlatilgan»ni BIRGA sanaydi, ro'yxat filtrida esa ular
  alohida edi → filtrga "Barcha muzlatilganlar" varianti qo'shildi → kartochka soni ro'yxat bilan teng chiqsin
- [2026-10-05] O'qituvchi tahririda o'rinbosar ham tahrirlay oladimi? → yo'q, faqat guruh egasi →
  o'rinbosar guruhga vaqtincha keladi, o'quvchi ma'lumoti esa doimiy
- [2026-10-05] O'qituvchi telefonni BO'SH qoldira oladimi? → ha (audit bilan) → "tahrirlash" talabi; noto'g'ri
  formatdagi YANGI raqam esa rad etiladi
- [2026-10-05] Sinfi kiritilmagan / oraliqqa tushmagan o'quvchi qancha to'laydi? → guruh (kurs asosiy) narxi →
  bazada 532 tadan atigi 57 tasida sinf bor; jimgina 0 yoki boshqa narx bo'lmasin
- [2026-10-05] Oraliqlar o'zgarganda allaqachon yozilgan oylar qayta hisoblanadimi? → yo'q (joriy oy faqat «Ha —
  joriy oydan» tanlansa) → kurs narxi o'zgarishidagi mavjud qoida bilan bir xil
- [2026-10-05] Maosh/tushum taqsimoti og'irliklari sinf narxiga o'tadimi? → yo'q, guruh narxida qoladi → maosh foizi
  baribir hisoblangan oylikdan; og'irlik faqat teglanmagan to'lovni bo'lish uchun
- [2026-10-05] «Chegirma» (kelmagan darslar) bir dars narxi qayerdan? → sinf oralig'i / kursning bir dars narxi,
  bo'lmasa oylik ÷ shu oydagi darslar → qisman oy hisobi bilan bir xil manba
- [2026-10-05] To'langan oyga chegirma? → rad etiladi, faqat to'lanmagan qoldiqdan ayriladi → aks holda jimgina avans
  bo'lardi; pul qaytarish — «Vozvrat»
- [2026-10-05] Chegirma qayerda? → admin to'lov oynasi ham, Kassa ham (ruxsat: to'lov ruxsati) → kassir to'lov paytida qo'yadi
- [2026-10-05] Vozvrat qaysi kassadan chiqadi? → asl to'lov tushgan kassadan (arxivlangan bo'lsa qaytaruvchining
  standart kassasi) → pulni qabul qilgan kassa qaytaradi, kassa qoldig'i izchil qoladi
- [2026-10-05] Kassa o'chiriladimi? → yo'q, faqat arxivlanadi; «Asosiy kassa» arxivlanmaydi → tarix va qoldiq
  yo'qolmasin, mas'ulsiz to'lov uchun zaxira kassa doim bo'lsin
- [2026-10-05] Bitta kassa bo'lsa to'lov oynasida «Kassa» maydoni? → ko'rsatilmaydi (2+ faol kassada chiqadi) →
  ortiqcha maydon, server baribir o'sha kassani tanlaydi
- [2026-10-05] Kassa mas'uli kim bo'la oladi? → xodim/admin/superadmin akkaunti (o'qituvchi emas) → to'lovni
  faqat panel akkauntlari kiritadi
- [2026-10-06] Guruh almashtirishda eski a'zolik holati? → guruhdan CHIQADI (IsActive=false, LeftAt), muzlatilgan
  bo'lib qolmaydi; hisob avvalgidek sanagacha qisman → foydalanuvchi: "muzlatilgan bo'lib qolmasin, butunlay chiqsin"
- [2026-10-06] Aktiv o'quvchini guruhdan chiqarishda oylik? → muzlatishdagi kabi: chiqish sanasigacha qisman, keyingi
  oylar bekor (oynada sana so'raladi) → "butunlay chiqib ketsin"; ilgari to'liq oy va oldindan yozilgan oylar qarz qolardi
- [2026-10-07] Maosh bazasi (uchrashuv: "to'langan puldan")? → rasmiy maosh HISOBLANGAN bazada qoladi, yonida
  "hozir chiqadigan (tushgan puldan)" + "qarzdorlar to'lasa yana +" ko'rsatkichi → foydalanuvchi: "3-usul, lekin
  taxminiy chiqadigan oyligi ko'rsatilib tursin"
- [2026-10-07] Davomat monitoringida «Sababsiz»? → sabab NOMIDA "sababsiz" so'zi bor qaydnomalar → alohida
  bayroq yo'q, sabablar katalogi matn bilan boshqariladi
- [2026-10-07] «Darsga keldi» xabari necha marta? → har dars uchun BIR marta (saqlashdan oldin keldi bo'lmaganlarga);
  qoida default O'CHIQ → ota-onaga har darsda SMS — pul, admin ongli yoqsin
- [2026-10-07] Botda o'qituvchi va o'quvchi uchun alohida tugmalarmi? → BITTA «📱 Panel» tugmasi, manzil sayt
  ildizi (RootRedirect rol bo'yicha yo'naltiradi) → foydalanuvchi: "bitta Panel deb nomlangan button yetadi"
- [2026-10-07] Lid «yo'qotilgan» — bosqichmi? → yo'q, lidning O'Z maydonlari (LostAt/LostReason...), ustun taxtada QAT'IY
  oxirgi → bosqichlar foydalanuvchi o'chiradigan narsa; tiklanganda lid avvalgi bosqichiga qaytadi
- [2026-10-07] Yo'qotilgan lidlar hisobotining davri? → lid KELGAN sana bo'yicha → foizlar bitta to'plamdan, ≤100%
- [2026-10-07] Yo'qotilgan lid formani qayta to'ldirsa? → yo'qotilgan qoladi, «Qaytib keldi» belgisi → avtomatik tiklash 5 ta
  kirish yo'lini o'zgartirardi
- [2026-10-07] HR voronkasidagi eski `review` bosqichi? → eski qatorlar uchun ko'rinadi, unga yangi o'tkazish yo'q → mavjud
  ma'lumot buzilmasin
- [2026-10-07] Arizani `hired` dan qaytarish moslashuvni bekor qiladimi? → yo'q → moslashuv alohida yuritiladi
- [2026-10-07] Karyera bo'limi kaliti? → `vacancies.list` (moslashuv — `vacancies.onboarding`) → faqat moslashuv berilgan
  xodim nomzod CV'larini o'qiy olmasin
- [2026-10-07] Kassadan kassaga ko'chirish kimda? → faqat superadmin; manbada yetarli pul bo'lmasa rad → kassir kamomadni
  qog'ozda boshqa kassaga o'tkaza olmasin
- [2026-10-07] Moliya turlari seed'i? → BIR MARTA (`SeedMarkers: finance-types-2026-10`), edutizimdagi eski turlar yangi
  toifaga ko'chiriladi → admin o'chirgan tur qayta tug'ilmasin; eski tranzaksiyalar eski toifasida qoladi (backfill yo'q)
- [2026-10-07] Kitob sotuvi Moliya'ga? → to'langan sotuv `book` kirimi bo'lib kassaga tushadi, qaytarish — chiqim; eski
  sotuvlar qo'shilmaydi → uchrashuv: "kitob sotuvi alohida to'lov turi"
- [2026-10-07] Ketish sababi qayerda saqlanadi? → alohida `MembershipReasonLog` (StudentGroup ustuni emas), tarix uchun audit
  matni zaxira → qayta qo'shilgan a'zolikka eski sabab yopishib qolmasin
- [2026-10-07] Boshqa kursga o'tish — ketishmi? → o'sha kurs uchun HA («Boshqa guruhda o'qiyapti» belgisi bilan), kurs
  ichidagi almashish — yo'q → bosh sahifadagi «Ketgan o'quvchilar» bilan bir xil son
- [2026-10-07] «Yangi o'quvchi» (qabul cheklisti)? → arxivlanmagan + sinovda yoki oxirgi 60 kunda qabul qilingan
  (EnrollmentDate) → edutizimdan ommaviy import qilinganlar «yangi» bo'lib chiqmasin
- [2026-10-08] O'quvchini arxivlash a'zolikka tegsinmi? → HA: arxiv = o'quvchi ketdi, har tirik a'zolik oddiy
  chiqarish yo'li bilan yopiladi (`LeaveGroupAsync`, sabab = arxiv sababi); tiklash guruhsiz → aks holda tiklashda
  arxivdagi oylar birdaniga hisoblanar va har oy uchun SMS ketardi (audit C1)
- [2026-10-08] Eski (yopilmagan) arxivdagi o'quvchilar ma'lumoti? → backfill YO'Q; o'qish yo'llari `IsArchived` ni
  himoya sifatida filtrlaydi, tiklashda tirik a'zoliklar `ArchivedAt` sanasida yopiladi → prod ma'lumoti jimgina o'zgarmasin
- [2026-10-08] Arxiv sababisiz arxivlash KPI'da? → "Sabab ko'rsatilmagan" (UnknownReasonFine) → KPI maqsadi — ketish
  sababini yozdirish
- [2026-10-08] To'lovi bor guruhni o'chirish? → 400, arxivlash taklif qilinadi; faqat to'lanmagan hisoblar qaytariladi →
  aks holda to'langan oylar balansga yolg'on avans bo'lib qaytardi
- [2026-10-08] O'qituvchi oy o'rtasida almashsa maosh? → guruhning oylik ulushi rejali dars kunlari bo'yicha
  `GroupTeacherHistory` egalariga mutanosib bo'linadi; tarix yo'q bo'lsa 100% joriy o'qituvchiga → ikki o'qituvchi
  ulushi yig'indisi bitta o'qituvchinikiga teng bo'ladigan yagona bo'linish
- [2026-10-08] Arxivdan qaytarilgan o'qituvchining arxivdagi oylari? → `Teacher.InactivePeriods` ga yoziladi va maoshga
  kirmaydi → tiklash ArchivedAt ni tozalab, qat'iy maoshni orqaga yozib yuborardi
- [2026-10-08] Sinovdagi o'quvchining to'lovi? → sinov guruhiga bog'langan AVANS, hisob yaratilmaydi → aktivlashtirish
  uni oddiy iste'mol qiladi; ilgari guruhsiz to'liq hisob paydo bo'lib, keyin o'chirilmasdi
- [2026-10-08] Bo'lingan to'lovni (guruh almashtirishda) bekor qilish? → butun oila (asl + `SplitFromId`) birga bekor
  qilinadi, qaytarish esa har qator alohida → bu bitta jismoniy to'lov; har bo'lak o'z guruhi maoshi/balansiga tegishli
- [2026-10-08] "Kech" sababi ota-onaga xabar? → hech qanday xabar yo'q (na "kelmadi", na "keldi") → o'quvchi kelgan,
  "keldi" xabari faqat aniq `Present` bayrog'iga bog'langan
- [2026-10-08] Mezon bo'yicha baholash va qo'lda qo'yilgan baho? → `JournalEntry.GradeFromCriteria` bayrog'i; sinxron faqat
  o'zi qo'ygan bahoni o'zgartiradi; eski ma'lumot migratsiyada heuristika bilan belgilandi (baho = o'sha kun belgilangan
  mezonlar soni) → ilgari har belgi qo'lda bahoni bosib ketardi, ya'ni teng qiymat deyarli doim sinxron baho
- [2026-10-08] O'rinbosar qoplagan darsni ko'chirish? → hech kim ko'chira olmaydi, avval o'rinbosarlik o'zgartiriladi →
  aks holda o'rinbosarga eski sana uchun haq to'lanib, yangi sanada yozish huquqi bo'lmasdi
- [2026-10-08] Kunlik eslatmalar restartda takrorlanmasin? → bitta `BackgroundJobRun` jadvali: har oluvchi yuborishdan
  OLDIN atomik belgilanadi + ish tugagach ish belgisi → yiqilishda ko'pi bilan bitta oluvchi o'tkazib yuboriladi,
  takror pullik SMS esa yo'q
- [2026-10-08] Restartda tugallanmagan ommaviy SMS? → 12 soatgacha davom ettiriladi, eskisi "uzilgan" bo'ladi →
  kechagi e'lon bugun ketib qolmasin
- [2026-10-08] Arxivdagi o'quvchiga avto-xabarlar? → faqat `payment_received` (to'lov kvitansiyasi) ketadi → eski
  qarzni to'lagan ota-ona kvitansiya olsin, davomat/baho xabarlari esa ketgan oilaga bormasin
- [2026-10-08] Guruhga bog'lanmagan eski qarz eslatmasi? → kurs nomisiz bitta umumiy eslatma → noto'g'ri guruh nomi
  yolg'on, jim o'tkazish esa qarzni unuttiradi
- [2026-10-08] Yuz bilan kirish veb/Mini App'da? → majburlanmaydi, istisno hujjatlashtirildi (`auth-sessions.md` §5) →
  yuz vektori telefonda hisoblanadi, vebda selfi oqimi yo'q; majburlash hammani saytdan chiqarib qo'yardi
- [2026-10-08] Qurilmani bekor qilish sessiyalarni yopadimi? → o'quvchining BARCHA refresh tokenlari bekor → tokenlar
  qurilma id'sini saqlamaydi, model o'zgarishi kerak bo'lmasin
- [2026-10-08] Lidni aylantirishda shu raqamli o'quvchi bor? → 409 + nomzodlar, "Mavjud o'quvchiga bog'lash" yoki
  "Baribir yaratish" → dublikat o'quvchilar oldi olinsin
- [2026-10-08] Aylantirilgan lid raqamidan yangi murojaat? → yangi lid ("Avval o'quvchi bo'lgan: <ism>" izohi bilan) →
  aka-uka/qayta yozilish pipeline'da ko'rinsin; Meta CAPI eski aylantirishni yangi reklama lidiga hisoblamaydi
- [2026-10-08] Topshirilgan natijasi bor test/formani o'chirish? → 400, faqat o'chirib qo'yish (`IsActive=false`) →
  lid kanallari va qabul cheklisti tarixdan yo'qolmasin
- [2026-10-08] Eski Instagram hodisasiga avto-javob? → DM 24 soat, izoh 72 soatdan eski bo'lsa javob yo'q (saqlanadi,
  telefon→lid ishlaydi, operatorga belgi) → modul yoqilganda eski izohlarga ommaviy ochiq javob ketmasin
- [2026-10-08] Agent o'chirilganda webhook? → obuna bekor qilinadi va ice-breaker'lar o'chiriladi (agent o'chiq paytdagi
  DM'lar CRM'ga tushmaydi) → har kiruvchi xabarga "akkaunt ulanmagan" ogohlantirishi ketmasin
- [2026-10-08] Bot orqali naqd kitob buyurtmasi? → tasdiqlash faqat zaxirani band qiladi, pul "To'landi" bosilganda
  kassaga tushadi; kelmagan buyurtma bekor qilinadi (pul harakatisiz) → pul HAQIQATAN olingan paytda yozilsin
- [2026-10-08] "Kamera kuzatuvini yoqish" o'chiq bo'lsa? → jonli ko'rish, klip va NVR qidiruvi yopiladi; ro'yxat va
  sozlash ochiq; yozuv bunga bog'lanmagan (o'z bosh kaliti bor) → kalit nomiga mos ishlasin
- [2026-10-08] Ishlatilgan davomat sababini o'chirish? → 400 (soni bilan), soft-delete emas → natija bir xil, migratsiyasiz
- [2026-10-08] Xodim o'chirilganda unga biriktirilganlar? → faqat TIRIK havolalar bo'shatiladi (ochiq lid, kassa
  mas'uli, tugallanmagan topshiriq, KPI profili, CTI agenti, Telegram) — yopilgan lid/tugagan topshiriq tarix bo'lib qoladi
- [2026-10-08] Telegram JSON zaxirasi? → maxfiy maydonlar NOMI bo'yicha olib tashlanadi, ~90 biznes jadvali qo'shildi,
  49 MB dan oshsa og'ir jadvallarsiz → yangi `*Token`/`*Password` maydon o'z-o'zidan chiqarib tashlanadi
- [2026-10-08] Arxivdagi (yopilgan) guruhga o'quvchi yaratish/import? → API 400; Excel importda o'quvchi guruhsiz
  yaratiladi + qator ogohlantirishi; shablonda faqat faol guruhlar → yopilgan guruhdan har oy hisob yozilmasin
- [2026-10-08] O'rinbosar haqi sanalari? → saqlangan sanalar guruh umri bilan qirqiladi, ko'chirilgan dars YANGI sanasida
  sanaladi; guruhning HOZIRGI hafta kunlari bilan filtrlanmaydi → kunlar o'zgarsa o'tgan (to'langan) oylar buzilmasin
- [2026-10-08] Guruh almashtirishda chegirma? → qator BO'LINADI (eski guruh qatori o'tish oyida tugaydi, yangi guruhga
  yangi qator) → o'tish oyidagi ikkala qisman hisob ham o'z chegirmasini saqlasin
- [2026-10-08] «Tugatish»da muzlatilgan a'zoning ketish sababi? → muzlatish sababi tizim yorlig'idan ustun → KPI
  "nazoratdan tashqari" sababni yo'qotmasin
- [2026-10-08] Yaratishda `active` bo'lgan o'quvchining birinchi oyi? → shu oyga to'lov kelganda qisman (prorate) hisob
  ochiladi (`EnsureChargeAsync`), yaratishda yozilmaydi → bepul oy bo'lmasin, ommaviy importda SMS to'lqini ham bo'lmasin
- [2026-10-08] Ommaviy davomat xabarlari? → bazadagi navbat (`AttendanceNotifyItems`), har oluvchi alohida, 6 soatdan
  eskisi yuborilmaydi → restartda yo'qolmasin, ertasi kuni kelgan "kelmadi" chalg'itmasin
- [2026-10-08] Standart Local SMS agenti o'chirilsa? → bloklanmaydi, ogohlantirish + SMS sozlamalarida banner → ketgan
  xodim telefonini darhol uzish muhimroq, lekin avto-SMS jim to'xtamasin
- [2026-10-08] Telegram zaxirasi formati? → `.json.gz` (oqimli siqish) → 1 GB RAM serverda xotira ikki baravar
  oshmasin va 49 MB chegarasiga sig'sin
- [2026-10-08] Instagram suhbati yangi lidga qayta bog'lansa reklama krediti? → suhbat lid tarixini saqlaydi
  (`IgConversation.PastLeadIds`), DM xabar kelgan paytdagi egasiga yoziladi → to'lagan eski lid reklamasini yo'qotmasin
- [2026-10-08] Bot belgisi (disclosure)? → faqat DM/shaxsiy javobda, ochiq izohda yo'q → bir xil uzun ochiq matn spam
  ko'rinmasin; Meta talabi xabar almashinuvga tegishli
- [2026-10-08] Qarz eslatmasida xato bo'lsa kun yopiladimi? → belgisi qo'yilmay xatoga uchragan qarzdor qolsa kun
  "bajarildi" bo'lmaydi, keyingi soatda faqat o'shalar qayta uriniladi
- [2026-10-08] Qo'lda ochilgan DB tranzaksiyasi? → faqat `CreateExecutionStrategy().ExecuteAsync` ichida (qayta urinishda
  holat bazadan qayta o'qiladi); guruhni o'chirish bitta `SaveChanges` ga o'tkazildi → `EnableRetryOnFailure` strategiyasiz
  tranzaksiyani rad etib, ikkala amal ham 500 berardi
- [2026-10-08] MoiZvonki kerakmi? → YO'Q (foydalanuvchi qarori): integratsiya, «Call Center — Bulut» sahifasi va
  `calls.cloud` ruxsati olib tashlandi; yagona telefoniya — Local Call (CTI). `Calls` jadvali (eski tarix) bazada
  QOLDIRILDI, DROP qilinmadi → prod'da bo'shligi tasdiqlansa alohida migratsiya bilan o'chiriladi
- [2026-10-08] KPI qo'ng'iroq ko'rsatkichlari manbasi? → Local Call yozuvlari (`CtiCallRecord`), xodim — agentning
  `StaffUserId` i orqali; "teginish" = kiruvchi + chiquvchi (javobsiz kiruvchi emas); yozuvni tinglash CTI'ning
  "faqat o'zinikini" qoidasi bilan → MoiZvonki olib tashlangach `Calls` to'ldirilmaydi, KPI jimgina bo'sh qolmasin
- [2026-10-09] Student gender "not needed" on the add form — remove it everywhere? → Removed from «Yangi o'quvchi»
  and the Excel template; kept in the profile «Tahrirlash» tab as an OPTIONAL select («Tanlang» = unspecified);
  empty/unknown gender is stored as `""` instead of silently becoming `"male"` (form, import, lead→student
  conversion) → user asked for gender to be optional, not deleted; existing rows untouched (no backfill)
- [2026-10-09] Patronymic («Otasining ismi») removed from the student form — what about students who already have
  one? → The field shows only when editing a student whose patronymic is non-empty, so it can be cleared → hiding
  it would keep it silently glued to F.I.SH with no way to remove it; existing names are not rewritten
- [2026-10-09] Dropping the «Jinsi» column from the import template shifts the columns — old filled files? →
  Import maps columns by HEADER text (`StudentImportPlan.MapColumns`), positional legacy layout only when the
  F.I.SH header is not found → old (11-column) and new (10-column) files both import correctly
- [2026-10-09] "Manual balance edit" — write the balance directly or adjust charges? → Adjust the chosen month's
  charge (existing superadmin `EditCharge`) behind a «Balansni tuzatish» modal with server preview + note →
  balance is derived from charges/payments in 25+ places (per-group balance, salary base, debtors); a free-floating
  balance adjustment would make those disagree. A per-student custom monthly price is the permanent fix for
  "same group, different tariff" and is left as a separate task.
- [2026-10-09] Backfill of active students left with a 0 balance — script or tool? → Superadmin tool
  (Moliya → «Qo'shilgan oy hisobini tuzatish»): preview list, apply only ticked rows; scope = current + previous
  activation month (same floor as `EnsureRecentActivationChargeAsync`); no auto-SMS → prod data change stays a
  reviewed, per-row decision instead of a blind script.
- [2026-10-09] Activation-date change — allow moving to a later month? → No (same month or earlier only) →
  moving later means voiding already-charged past months; freeze/remove already cover that.
- [2026-10-09] «Buyurtmalar» vs «Birinchi dars» overlap → table default hides first-lesson and converted leads,
  kanban keeps the whole pipeline → client: a lead moved to Birinchi dars must leave Buyurtmalar; the kanban is
  a stage board where every stage column must stay populated.
- [2026-10-11] CRM upgrade (client meeting script) — USER DECISIONS, not assumptions (full gap list:
  `docs/GAP-crm-takomillashtirish.md`):
  1. Remove entirely: push, group chat, Local Call, Local SMS (CTI agent app), APK — no installable app is used.
  2. Remove the SMS channel entirely (Eskiz too) — Telegram is the only outbound channel.
  3. Absent notice: the send time is configurable by admins (settings).
  4. Teacher fine for attendance not taken; the delay after which it applies is an admin setting (e.g. 10 / 15 min).
  5. Tests: no question authoring / distribution in the platform (remove it if present). Only results are entered
     per group: name, date, number of questions, number solved, percent on a 100-point scale; 1–2 times a month.
     Results must feed the rating.
  6. No debtor lock on the rating. Student profile shows rich rating data (own tests, history, monthly, group,
     centre, quarterly / 3-month, yearly) — clean, not cluttered.
  7. Student login + password permanently visible to administrators; per-group login/password export.
  8. "Telegram connected / not connected" column in the active students list and in the teacher's view of his students.
  9. FIRST PRIORITY: an MCP server for the whole project — STRICTLY READ-ONLY (no write / delete / create tools).
- [2026-10-11] MCP: how to build it? → Port the sibling WunderkindLMS design (`SchoolLms.Server/Mcp`): remote
  Streamable-HTTP MCP at `/mcp` inside the existing backend, OAuth 2.1 + PKCE login on our own page, leadership only
  (`superadmin`, `admin`, staff with the `aiAccess` permission), tools run on a separate DbContext bound to a
  SELECT-only PostgreSQL role, fail-closed when that connection is not configured → same owner, same stack family,
  already proven with claude.ai / ChatGPT connectors.
- [2026-10-11] Test percent vs solved count — two inputs or one? → One input (solved count); percent is computed
  as solved / questions × 100 → the client's example (80% and 25 of 30) was approximate; two independent inputs
  would let them disagree.
- [2026-10-11] "Remove test authoring" — how far? → Remove the ONLINE mode of group tests (bot PDF, answer key,
  test code, external participants). The lead level test and curriculum-item tests are left in place for now and
  flagged to the user → they belong to the lead funnel / curriculum, not to "monthly tests".
- [2026-10-11] Fine amount for attendance not taken → configurable in settings (sum per lesson), next to the delay
  → the client named the delay but not the amount.
- [2026-10-11] CRM SPEC: how do 5 agents avoid editing the same files? → Phase 0 prep (partial entity/DbContext/controller classes, per-phase `ConfigureCrm*` partial methods, `Crm*Setup` DI hooks, pluggable EF interceptors); one migration per phase generated only at merge time → `Entities.cs`, `AppDbContext.cs`, `Program.cs` and the model snapshot are the conflict hotspots (`docs/SPEC-crm-upgrade.md` §0, §8).
- [2026-10-11] Parent/student Telegram delivery — direct send or queue? → DB outbox `MessageDelivery` (queue + per-recipient log) with a throttled worker → needed for the pre-send count, ack status, blocked-chat tracking, restart safety and Telegram rate limits.
- [2026-10-11] «Ko'rdi / Qabul qildi» — two statuses? → one status, set by the «✅ Qabul qildim» button (callback prefix `mack:`) → Telegram gives bots no read receipts.
- [2026-10-11] Which Telegram account is father/mother/student? → registration matches all 4 numbers (`TelegramPhoneMatchField` ignored); relation detected Father → Mother → ParentPhone → student Phone, else "other", admin-editable → parents are the likely owners of a shared number.
- [2026-10-11] «Barcha ota-onalar» default audience → non-archived students with a live ACTIVE or TRIAL membership, relations father/mother/parent/other, blocked chats excluded and counted; frozen is opt-in → trial students attend lessons, frozen ones are on pause.
- [2026-10-11] One announcement to a parent with 2 children → one message per chat when the template has no student tokens, else one per (student, chat) → no duplicate identical messages.
- [2026-10-11] Message attachments → one file per message (jpg/png ≤ 10 MB, mp4/pdf/office ≤ 50 MB), private `uploads/messaging`, Telegram file_id cached, local copy deleted after 60 days once uploaded → Bot API limits and the 1 GB server.
- [2026-10-11] Debt reminder schedule → each `payment_debt` rule has its own day-of-month + time (more days = more rules); existing rules migrated to day 1 at 09:00; odd-day cadence removed → client: "har oyning ma'lum sanasida".
- [2026-10-11] SMS/push-only auto rules after Telegram-only → disabled at deploy (names logged); afterwards `Enabled` alone decides → do not silently start Telegram messages nobody configured.
- [2026-10-11] Test results to parents — on every score save or manual? → manual «Ota-onalarga yuborish» per test, re-sends only changed scores → scores are typed one by one; per-save messages would spam with shifting ranks.
- [2026-10-11] TOP/BOTTOM 5 basis → previous month's TEST average (not the composite), per group by default, k = min(5, ⌊n/2⌋), scope switchable → client: "test natijalariga ko'ra"; ⌊n/2⌋ stops small groups from warning almost everyone.
- [2026-10-11] Rating formula → GroupScore = 60 % average test percent + 40 % ball normalised to the group's best (weight configurable); centre score = mean of group scores; competition ranking (1,2,2,4) → owner: tests must feed the rating; normalisation removes grading-volume differences between groups.
- [2026-10-11] Rating periods "3-month" and "quarter" → merged into one rolling 3-month period, plus month and academic year (Sep–Aug) → owner: "juda maydalashib ketmasin"; a rolling window always has data.
- [2026-10-11] Attendance — one definition → late = attended; graded or explicit "keldi" = present; attendance-taken + no mark = present; conducted-only + no mark = unmarked (excluded from %) → journal.md rule, honest percentages; the retroactive change is accepted.
- [2026-10-11] When is attendance "taken" for reminders, fines, monitor? → `LessonNote.AttendanceTakenAt` = first present/absent mark (single or bulk); `AttendanceTaken` = complete (bulk or every expected student marked) → one grade must not count; cell-by-cell teachers must not be fined.
- [2026-10-11] "Teacher must mark during the lesson" — block late entry? → no block; enforced by repeated reminders, admin escalation, a fine after start + N min and manager approval of late-entered absences → blocking would leave lessons unmarked and prevent corrections.
- [2026-10-11] Where is the attendance fine recorded? → `TeacherAttendanceFine` rows deducted inside `SalaryLedger` (Expected), listed on Moliya → Jarima «Davomat jarimalari», waivable; NOT a `FinanceTransaction` → a fine is not cash movement and must not touch kassa/P&L.
- [2026-10-11] Who is fined on a substitute day? → the substitute (the person teaching that day) → same rule as the attendance reminder.
- [2026-10-11] Absent-notice manager control — default → mode `late_only`: on-time attendance notices go automatically at lesson end + delay; notices from late attendance wait for approval and expire unsent if nobody acts → no wrong «kelmadi» while keeping automation.
- [2026-10-11] «Keldi» (present) notices → stay immediate, never held → only «kelmadi» is the risky message.
- [2026-10-11] Excused/unexcused backfill → `IsExcused = !IsLate && name does not contain "sababsiz"`; editable afterwards even when used → mirrors today's heuristic; the flag only affects analytics.
- [2026-10-11] Consecutive unexcused absences → per (student, group); excused or late breaks the streak, unmarked lessons are skipped; levels 3 and 5; automatic «Bog'lanish kerak» only if the last absence is ≤ 14 days old; one open request (else a note) → no duplicates, no stale flood after deploy.
- [2026-10-11] «Bog'lanish kerak», «Testlar natijalari», «Topshiriqlar» in the menu → moved from Future to the main menu (O'quvchilar, O'quv bo'limi, Boshqaruv) → the new flows create daily work there.
- [2026-10-11] Double booking → hard 409 for everyone; only a superadmin may force, with a reason (audited); checked only when schedule fields change; date ranges considered; substitute assignments stay warning-only → client: lessons must not collide; existing conflicts must not block unrelated edits.
- [2026-10-11] Weekday-template schedule tabs → replaced by date-based «Kun» / «Hafta» views on the home page and «Dars jadvali» → client asked for real daily/weekly views; two schedule UIs would confuse.
- [2026-10-11] Task approval → `ApprovalRequired` on by default when assignee ≠ creator; approver = creator or admin/superadmin; returning needs a reason; new boards no longer get «Tekshiruvda» → "bajarildi / tasdiqlandi" monitoring; existing tasks and boards untouched.
- [2026-10-11] Home widgets storage → per-user `UserSettings.HomeLayout` JSON on the server; defaults = today's page (12 cards + schedule) → per-user and cross-browser; edutizim parity by default.
- [2026-10-11] Permanently visible student passwords → AES-256-GCM vault table `StudentCredentials` (key `CREDENTIAL_VAULT_KEY` in .env, `FaceVault` pattern) filled by a SaveChanges interceptor on every set/reset/self-change; fail-closed without the key; students only → DataProtection keys (dpkeys volume) are outside backups; the interceptor makes a forgotten call site impossible.
- [2026-10-11] Where are logins/passwords shown? → profile card, group «Login/parollar» modal and per-group Excel export (audited); never inside list payloads → convenient without leaking passwords in every list response.
- [2026-10-11] New permission keys for the CRM upgrade → only `students.notices` (absent-notice control page) and `classes.rating` (rating report); everything else reuses existing keys → "own route = own key" (permissions.md §10).
- [2026-10-11] Achievements list page? → none; achievements live in the student profile, the cabinet and MCP → no new page/key for a small catalogue.
- [2026-10-11] MCP I7: where to store "superadmin enabled AI for this admin"? → `Users.AiAccessEnabled` bool
  (no backfill: existing admins lose AI access until a superadmin enables them) → `IsAllowed(AppUser)` stays a
  pure check on the loaded user; the column is outside the read-only role's Users allow-list.
- [2026-10-11] MCP L8: where do users see their own AI connections? → «Mening AI ulanishlarim» block under
  Akkaunt sozlamalari (profile page + Sozlamalar → Umumiy → Foydalanuvchi sozlamalari) → that is where a user
  manages their own login/password; the superadmin-only «AI ulanishlar» tile stays superadmin-only.
- [2026-10-11] MCP volume alert: revoke the connection automatically? → No, flag + Telegram + warning log only
  → a heavy legitimate report must not cut the director off; the superadmin decides.
- [2026-10-11] Phase 1 cleanup — what happens to SMS-only auto-message rules? → Rules saved with `SendSms` are
  delivered over Telegram (`AutoMessageChannels.Telegram`); the next save clears the flag → birthday greetings,
  debt reminders etc. keep reaching families without the admin re-configuring every rule.
- [2026-10-11] Phase 1 — cabinet (bell) notification was written only for push-enabled rules; now? → Written for
  EVERY enabled rule when the recipient has an account → no rule loses its in-app history when push disappears.
- [2026-10-11] Phase 1 — SMS-only lead flows (lead_new, trial_reminder, test_link, test_result, single/bulk lead SMS)?
  → Removed, not converted → a lead has no Telegram recipient; old rules stay inert (hidden as unknown triggers).
- [2026-10-11] Phase 1 — "send level test" without SMS? → The invite link is still created and returned to the
  operator (copy + send via Telegram/WhatsApp), `SmsStatus = "link"` → the funnel keeps working with no new channel.
- [2026-10-11] Phase 1 — KPI call items (missed calls returned, answer speed, touches/talk-time norms, weekly call
  sample)? → Removed / turned into manual checklist items → their only data source (Local Call agent app) is gone;
  guessing would fine staff wrongly (bonus impact).
- [2026-10-11] Phase 1 — removed tables/columns? → CODE ONLY: entities stay mapped and marked `[Obsolete]`, no
  migration, no drop → a drop is a separate, confirmed step (data loss).
- [2026-10-11] Phase 1 — CTI agent rows when a staff member is deleted? → Left untouched → agents can no longer
  connect (`/ws`, `api/cti/*` removed); rewriting legacy rows adds risk without benefit.
- [2026-10-11] Phase 1 — APK files under `uploads/app-*.apk` and the `cti-recordings` volume? → Left in place
  (data) → deleting files is a separate, confirmed step.
- [2026-10-12] U6a — vault key missing and an admin sets/resets a student password? → the stale vault row is
  DELETED (never kept, never plain text); the new password stays visible from `InitialPassword` until the first
  login → a kept row would show a WRONG password once the key returns.
- [2026-10-12] U6a — vault row and a still-unused `InitialPassword` disagree? → `InitialPassword` wins → it is
  written together with the hash and cleared on every login/own change, so it is always current; a differing row
  can only be stale.
- [2026-10-12] U6a — per-group Excel when a password is unknown / a member has no account? → Parol cell
  «(saqlanmagan)», Login cell «(akkaunt yo'q)»; the window offers «Paroli noma'lumlarga yangi parol yaratish (n)»
  (n = unknown + no account) → existing students' passwords cannot be recovered from the hash.
- [2026-10-12] U6a — `POST …/credentials/ensure` with `onlyUnknown: false`? → 400 → regenerating KNOWN passwords
  of a whole group in one click is destructive and nobody asked for it.
- [2026-10-12] U6a — audit action of the group login/password export? → `create` on `Group` (a file was produced),
  summary «Login/parollar eksport qilindi: {guruh} ({n} ta o'quvchi)», no password → the audit has only 4 actions;
  the contract download uses `create` too.
- [2026-10-12] U6a — «Parollarni saqlash o'chiq» banner — where? → in the profile «Kirish ma'lumotlari» card and
  the group «Login/parollar» window (env-key text for the superadmin, a generic text for others) + a startup
  warning log → those are the only places a missing key changes what admins see; no global banner.
- [2026-10-12] U6a — Telegram column of the group export before U6b? → counts DISTINCT linked chats per student
  from `TelegramRegistrations` («Ulangan (n)» / «Ulanmagan»); blocked chats are not known until U2a/U6b → U6b may
  refine to usable chats.
- [2026-10-12] U6a — a deleted student account (any path, not only `StudentPurge`)? → the interceptor also
  deletes its vault row → no orphan encrypted passwords.
- [2026-10-12] U6a — `McpReadOnlyGrants.Forbidden` += `StudentCredential` (SPEC P6.3)? → left to U6m (MCP unit,
  "do not touch MCP") → already closed today: the column name contains "Password" and no tool lists the table.
- [2026-10-12] U4b rating — attendance line of the student block: `AttendanceRules` (U3a) is not merged yet → use `StudentJournalBuilder.PeriodAsync` (the student app's own numbers, one student's rows only) → U3a moves that builder to the single definition, so the block follows without a second copy.
- [2026-10-12] U4b rating — `tests[].kind` before U4a adds `TestResult.Kind` → returned as "" (badge hidden) → U4a is a later unit; percent = Score ÷ MaxScore already works unchanged once MaxScore = QuestionCount.
- [2026-10-12] U4b rating — `GET api/admin/rating/top-bottom` → left to U4d (needs `TopBottomRules`, U4d's file) → avoid writing U4d's rule twice; settings for it (count/scope) are stored now.
- [2026-10-12] U4b rating — "previous period" of «3 oy» for the ▲/▼ arrow → the three months right BEFORE the window (Aug–Oct vs May–Jul), not the window shifted by one month → comparing overlapping windows would mostly compare the same data.
- [2026-10-12] U4b rating — centre-level T (components, centre table) → mean over ALL the student's tests (each test equal weight), not the mean of group T's → same "each test equal weight" principle as the group T.
- [2026-10-12] U4b rating — who is in a group's table → students with a test or ball in the period (any membership state) first; current active/trial members without data follow unranked («—») → the teacher sees who has nothing yet; frozen members are omitted.
- [2026-10-12] U4b rating — B for a student with tests but no ball rows in a group whose best ball > 0 → B = 0 (counts) → SPEC: B = Ball ÷ best × 100, absent only when the group's best is 0.
- [2026-10-12] U4b rating — rank inside a single test (block «Testlarim») → among everyone scored in that test, archived included → same rule as `TestResultService` (detail/student results).
- [2026-10-12] U4b rating — cache key → `rating:v2:{kind}:{key}:{to}` (SPEC key + clipped end date) → the current period grows daily; without `to` a cached month could miss today's data for up to 15 min after midnight.
- [2026-10-12] U4b rating — `CenterMeta.RatingTestWeight` default for the existing row → set to 60 by data SQL in the migration (`docs/migrations/Phase4.sql`), no `HasDefaultValue` in the model → 0 is a valid weight and EF would silently drop an explicit 0 on INSERT with a DB default.
- [2026-10-12] U4c rating UI — cabinet top lists → per group: top 10 + own row; centre: top 10 + own row; other students shown with name, rank and score only (no id) → SPEC shape; ids are not needed by the cabinet.
- [2026-10-12] Phase 5 — one conflict entry per group when both room AND teacher collide? → One entry, kind
  `room` first (same as the old warning and its tests) → the list names the group once; the room is the stronger signal.
- [2026-10-12] Phase 5 — reschedule clash: which teacher counts on the target day? → The person who TEACHES
  that lesson (substitute if any, else the owner), on both sides → an owner covered by a substitute is free then.
- [2026-10-12] Phase 5 — superadmin force on reschedule, how does the client know? → Admin journal answers 409
  `{ roomConflict, message, canForce }` (`ScheduleConflictException`); the teacher endpoint keeps 400 + text →
  only the admin journal may force (SPEC), teacher UI unchanged.
- [2026-10-12] Phase 5 — dated schedule "attendanceTaken" before Phase 3's `AttendanceTakenAt` exists? →
  `LessonNote.Conducted` (the lesson monitor's definition) → one definition until U3a replaces it everywhere.
- [2026-10-12] Phase 5 — grid/list toggle vs SPEC URL `view=day|week`? → `view` = Kun/Hafta, grid/list moved to
  `?layout=list` → both kept, no key clash; old `?day=` links simply open today.
- [2026-10-12] Phase 5 — old weekday `ScheduleBoard`/`HomeFilterRow` components? → Kept (no page uses them),
  `schedule/grid` endpoint and `export?day=` kept → additive; removal is a separate cleanup.
- [2026-10-12] Phase 5 — bot «✅ Bajardim» must notify the creator: edit beyond the single `wtok:` branch? → One
  extra line in `HandleWorkTaskDoneAsync` (`WorkTaskFlow.OnCompletedAsync`) → SPEC P5.2 requires the bot path to
  call the single approval method; logic stays in WorkTaskFlow.
- [2026-10-12] Phase 5 — `wtok:` refusal "toast"? → Plain bot message «Bu amal sizga ruxsat etilmagan» → the shared
  callback handler already answers `answerCallbackQuery` without text before branching.
- [2026-10-12] Phase 5 — return an already APPROVED task? → Allowed (approval withdrawn) → the approver may have
  approved by mistake; approve itself is refused twice.
- [2026-10-12] Phase 5 — approval/return notices: Telegram only? → Telegram + cabinet bell for the recipient →
  staff without a bot link still see it; bell rows are saved with the same SaveChanges.
- [2026-10-12] Phase 5 — home widgets: blocks of later phases (absentNotices, redList)? → In the server catalog now
  (layout keeps their place), client marks them `available: false` until U3b/U3d build them → later phases add
  only a component.
- [2026-10-12] Phase 5 — home layout 4 000-char limit: before or after normalisation? → On the SENT payload →
  normalised output is always small; the limit only stops garbage payloads.
- [2026-10-12] Phase 5 — home layout writes audited? → No (personal view preference, like theme/language).
- [2026-10-12] Phase 2 — SPEC startup fix "disable SMS-only rules" vs Phase 1 "SMS rules go over Telegram"? → Follow the merged Phase 1 decision: only rules with NO external channel (push-only) are disabled (names logged); legacy SMS rules stay enabled; then `SendTelegram = true` everywhere → Phase 1 is the later, user-facing decision.
- [2026-10-12] Phase 2 — `{link}` manual-send block (`MessageTokenCatalog.ForbiddenInManual`) no longer exists (Phase 1) → `{link}` is reported as an unknown token («Noma'lum o'zgaruvchi: {link}») → same 400, one rule.
- [2026-10-12] Phase 2 — achievement resend counter (`{id}:{n}`) storage? → new column `StudentAchievement.CongratulationCount` (int, default 0) → the dedupe key must grow per explicit resend and survive restarts.
- [2026-10-12] Phase 2 — campaign while the bot token is missing? → 400 «Telegram bot sozlanmagan (.env: TELEGRAM_BOT_TOKEN)»; auto messages are still queued (and expire unsent) → a manual send must not silently go nowhere; the auto log stays honest.
- [2026-10-12] Phase 2 — achievement created with «Ota-onaga tabrik yuborish» but no enabled `achievement` rule / no linked chat? → the achievement is SAVED and the response carries a `warning` (200), never a 400 → a 400 after saving would make the user resubmit and create a duplicate.
- [2026-10-12] Phase 2 — group context of campaign tokens ({guruh}, {oqituvchi}, {dars_*})? → the one selected group when exactly one group is filtered (and {guruh} = its name), otherwise the student's main (ClassName) group → same as the legacy broadcast.
- [2026-10-12] Phase 2 — `audience/count` for debt campaigns? → `kind` is an optional field of the count request; the count always runs the SAME planner as `POST campaigns` (no rendering) → "count == rows created" holds for every kind.
- [2026-10-12] Phase 2 — «students» audience of auto rules → only chats with relation `student`; «parents» → every other relation (incl. not-yet-backfilled "") → mirrors the campaign relation chips.
- [2026-10-12] Phase 2 — multi-part (media + long text) retry after the media part went out? → a pending row with `TelegramMessageId` set resends only the text → no duplicate media on retries.
- [2026-10-12] Phase 2 — RateLimited pause → the whole worker pauses for `retry_after` (capped at 60 s; the row waits up to 300 s) on every 429 → Telegram's descriptions do not distinguish chat from bot flood reliably.
- [2026-10-12] Phase 2 — student purge and the new tables? → achievements and the student's delivery-log rows are deleted with the student (listed in the purge preview) → no orphan rows (student-purge.md §7).
- [2026-10-12] Phase 2 — legacy `POST messages/broadcast` with both students and teachers selected → two campaigns (students + teachers), one combined `BroadcastDto` → the old request could mix them; a campaign has one audience.
- [2026-10-12] U3a — which "attendance taken" drives the client/journal default «keldi»? → `LessonNote.AttendanceTaken` (complete), exposed as `GroupJournalDto.AttendanceTakenDates`; `AttendanceTakenAt` (first mark) only for reminders/monitor/fines/notices → a single first mark must not turn every unmarked student into «keldi».
- [2026-10-12] U3a — `AttendanceTakenAt` history after deploy? → backfilled for the last 60 days as the lesson DATE 00:00 when the lesson had bulk attendance or any «keldi»/reason mark (docs/migrations/Phase3.sql) → keeps the monitor's past days correct and makes the fine job's "yesterday" scan never fine pre-deploy lessons.
- [2026-10-12] U3a — Analytics.BuildClass (group performance %) had no membership bounds → now passes `StudentJournalBuilder.BoundsOf` per student (optional parameter) → the same window as the journal/profile; percentages of students who joined mid-month no longer count lessons before they joined.
- [2026-10-12] U3a — StudentReportBuilder / profile monthly absence charts and StudentPortal absence list → left as is (they already count non-late reasons as absences and late separately, consistent with AttendanceRules); `StudentBallService` teacher rating attendance left to U4b (it rewrites the rating) → avoids merge conflicts with Phase 4.
- [2026-10-12] U3a — group page grid: graded + «kech» cell → shows «kech» (reason wins, as on the server); explicit «keldi» inside a blocked (outside membership) date still shows green → display unchanged for explicit marks; counts follow the server rule.
- [2026-10-12] U3c — which groups can be fined/reminded? → only lessons with a start time, a course (journal exists) and at least one student EXPECTED that day; the earliest lesson of the group's day; archived teachers are never fined → a group nobody can take attendance for must not produce fines.
- [2026-10-12] U3c — escalation recipients' Telegram → `TelegramRegistration.UserId` of superadmin/admin/staff with `classes.notAttended`; the bell (UserNotification) is always written → SPEC P3.4; staff without the page key gets nothing.
- [2026-10-12] U3c — admin teacher profile «Davomat jarimasi» date list → loaded from `api/admin/attendance-fines` only when the viewer has `finance.main` (the amount/count line itself comes from the salary ledger) → the fines endpoint is ReadRequiresPerm.
- [2026-10-12] U3c — teacher portal fines endpoint gate → `TeacherPermissions.Salary` (same as the salary page) → fines are salary data.
- [2026-10-12] U3d — which new alert triggers the contact action? → only alert rows INSERTED in this run (a new level of a new streak); at most one action per student per run (highest level, newest absence) → reruns never add notes; a 3 → 5 progression adds exactly one note.
- [2026-10-12] U3d — stale streaks (> 14 days) → the alert row is still written (so it never fires later) but no request/note → no flood after deploy.
- [2026-10-12] U3d — KPI «Ketma-ket 3 dars kelmaganlar» → now from AbsenceStreakLoader, per student the longest group streak → the old rule counted every non-«keldi» entry (graded, late) across groups.
- [2026-10-12] U3d — red list `min` parameter → any value ≥ 1 accepted (UI uses 3 and 5); default 3 → harmless, lets KPI-like "2" lists reuse it.
- [2026-10-12] U3d — profile absences endpoint permission → class gate of StudentsController (`students.list`, GET readable by staff like the rest of the profile) → no sensitive data (dates/reasons only); SPEC names no separate key.
