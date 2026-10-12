---
description: Jurnal tahrirlash siyosati (JournalPolicy) va BALL / reyting hisobi.
paths:
  - "WunderkindLC.Application/Services/Journal*.cs"
  - "WunderkindLC.Application/Services/StudentBallService.cs"
  - "WunderkindLC.Application/Services/RatingService.cs"
  - "WunderkindLC.Application/Services/GradingService.cs"
  - "WunderkindLC.Server/Controllers/JournalController.cs"
  - "WunderkindLC.Server/Controllers/ClassAnalyticsController.cs"
  - "WunderkindLC.Server/Controllers/TeacherPortalController.cs"
  - "WunderkindLC.Server/Controllers/StudentAttendanceController.cs"
  - "WunderkindLC.Application/Services/AttendanceNotify*.cs"
  - "WunderkindLC.Application/Services/Attendance/AbsentNotice*.cs"
  - "WunderkindLC.Server/Controllers/AttendanceNoticesController.cs"
  - "WunderkindLC.Client/src/pages/admin/nazorat/AbsentNoticesPage.tsx"
  - "WunderkindLC.Client/src/pages/admin/journal/**"
  - "WunderkindLC.Client/src/pages/admin/classes/**"
  - "WunderkindLC.Client/src/pages/admin/grading/**"
---

# Jurnal va reyting qoidalari

- **TO'LOV NAZORATI (jurnal "darvozasi")** — migratsiya `AddJournalPaymentGate`. "Guruhlar →
  Jurnal boshqaruvi" oynasidagi ikkita mustaqil sozlama (`CenterMeta`):
  `JournalHideUnpaidPrevMonth` (o'tgan oy(lar)dan qarzi bor) va `JournalHideUnpaidAfterDay` +
  `JournalUnpaidCutoffDay` (1..28; joriy oy qarzi + shu kun kelgan). Ikkalasi ham default
  **o'chiq** — yoqilmasa xatti-harakat o'zgarmaydi.
  **BU MUZLATISH EMAS:** a'zolik, oylik hisoblash, qarz/balans hammasi davom etadi; faqat
  O'QITUVCHI jurnalida qator ko'rinmaydi va o'qituvchi yoza olmaydi. To'lov tushishi bilan
  qator O'Z-O'ZIDAN qaytadi (holat balansdan real vaqtda hisoblanadi, qo'lda "ochish" yo'q).
  Yagona qoida — `JournalPolicy.PaymentGate(policy, balanceInfo, currentMonth, today)` →
  `(Hidden, Reason)` bunda Reason = `prevMonth` | `cutoff`. Manba:
  `GroupBalanceService.GroupBalanceInfo.OldestDebtMonth` / `.DebtThisMonth` (mavjud qarz-oy
  siklida hisoblanadi, QO'SHIMCHA SO'ROV YO'Q).
  Qo'llanish: `JournalService.GroupMonthAsync` faqat BAYROQ qo'yadi
  (`GroupJournalStudentDto.PaymentHidden/PaymentHiddenReason`) — **admin jurnalidan hech kim
  olib tashlanmaydi** (admin belgisi bilan ko'radi); `TeacherPortalController` esa
  `journal/group` javobidan bunday o'quvchi va uning yozuvlarini OLIB TASHLAYDI, katak
  PUT/DELETE'ni 400 bilan rad etadi, `bulk-attendance`da ularni CHETLAB O'TADI.
  DIQQAT: qoida BUGUNGI holatga qarab ishlaydi (ko'rilayotgan oyga emas) — qarzdor eski oy
  jurnalida ham ko'rinmaydi, to'lagach hamma oyda birdan qaytadi.
  Qamrovda EMAS (kerak bo'lsa alohida qo'shiladi): topshiriq natijalari (o'quv dasturidagi
  `CourseItem` urinishlari), chat — u yerlarda o'quvchi ko'rinaveradi.

- **Jurnal boshqaruvi (tahrirlash siyosati):** Guruhlar sahifasida "Jurnal boshqaruvi" tugmasi → modal
  (`JournalPolicyModal`). Sozlama `CenterMeta`da (migratsiya `AddJournalPolicy`): `JournalEditMode`
  ("free"|"today"|"window"), `JournalRetroDays` (window uchun 1-90), `JournalConductedOnly` (baho faqat
  "o'tildi" darsga; ommaviy davomat mustasno — u darsni o'zi conducted qiladi), `JournalApplyToAdmins`
  (default false — faqat o'qituvchiga). Nazorat nuqtasi: `JournalPolicy.CheckAsync` (Application/Services)
  — admin `JournalController` va o'qituvchi `TeacherPortalController`ning PUT/DELETE journal +
  bulk-attendance'da chaqiriladi, taqiq 400+message. API: `GET/PUT /api/admin/journal/policy`
  (AdminPerm "classes"). Kelajak sanalar HAR DOIM taqiq (JournalService).

- **Muzlatilgan o'quvchi jurnalda:** alohida yig'iladigan blokda ko'rinadi (faqat o'qish). Blok
  BALANS rangini faol qatorlar bilan bir xil ko'rsatadi (to'lagan — yashil nuqta+ism, qarzdor — qizil)
  va ism yonida "Muzlatilgan" yorlig'i turadi. Guruhda FAOL o'quvchi qolmasa (masalan guruh yopilgan)
  blok avtomatik OCHIQ bo'ladi. TUGATILGAN (arxiv) guruh jurnalida oylar ro'yxati arxiv oyida
  to'xtaydi (`JournalService.GroupMonthAsync`) — bo'sh joriy oy ochilmasin. Muzlatilgan
  sanadan KEYINGI o'tilgan darslar avto-"keldi" ✓ EMAS (faqat aniq belgilangan `entry.present` yashil);
  guruhga qo'shilishidan (`memberStart`) yoki guruh `startDate`idan OLDINGI darslar ham hech qachon
  avto-"keldi" bo'lmaydi.

- **ORQAGA SANALGAN A'ZOLIK — `StudentGroup.RecordedAt`:** `JoinedAt`/`ActivatedAt` orqaga
  sanalishi mumkin (o'quvchi bugun qo'shilib, o'tgan oydan aktivlashtiriladi), `RecordedAt` esa
  a'zolik HAQIQATDA tizimga kiritilgan kun (hech qachon orqaga sanalmaydi). Standart
  "davomat olindi + yozuv yo'q = keldi" qoidasi FAQAT `RecordedAt`dan keyingi darslarga
  qo'llanadi — undan oldingilari BO'SH qoladi (o'qituvchi qo'lda belgilaydi), chunki o'sha
  paytda bu o'quvchi guruhda yo'q edi va `BulkAttendanceAsync` uni chetlab o'tgan.
  Qo'llanadigan joylar: `JournalService.GroupMonthAsync` → `PresentDefaultFrom` (admin va
  o'qituvchi guruh jurnali) VA **`StudentJournalBuilder`** — o'quvchining O'Z QATORI uchun
  yagona yadro: `GroupMonthAsync` (admin profilidagi jurnal modali,
  `GET /api/admin/student-attendance/journal`) va `PeriodAsync` (o'quvchi ilovasining
  «Umumiy statistika» ekrani, `GET /api/student/journal?from=&to=&groupId=`) shundan quriladi.
  **Yangi jurnal ko'rinishi qo'shilsa — shu cheklovni ham qo'shish SHART** (eng yaxshisi —
  `StudentJournalBuilder`ni chaqirish, mantiqni nusxalamaslik).
  O'quvchi profilidagi modalda bunday NOMA'LUM (yozuvsiz, RecordedAt'dan oldingi) darslar
  jamlanma sanog'iga ham kirmaydi — aks holda davomat foizi asossiz tushardi.
  `RecordedAt` a'zolik yaratilgan/aktivlashtirilgan HAR bir joyda to'ldirilishi shart
  (`ClassesController` AddMember/ActivateMember/TransferMember/guruhni bo'lish,
  `StudentsController`, `LeadsController`) — bo'sh qolsa "cheklov yo'q" deb talqin qilinadi. "Davomat" tabi ham shu chegaralarda sanaydi:
  `held = conducted ∩ (memberStart ≤ sana ≤ frozenAt)` — o'quvchi portali (`StudentAttendanceController`
  `memberEnd`) bilan bir xil.

- **BALL (reyting bali)** = `Σ(QO'LDA qo'yilgan jurnal baholari) + Σ(bajarilgan baholash mezonlari)` — guruh
  sahifasidagi "Reyting" tabi bilan BIR XIL formula (klient: `lib/studentBall.ts` → `ballGrade`; admin guruh
  sahifasi ham, O'QITUVCHI ilovasining jurnal jami/saralashi va «Reyting» tabi ham — 2026-10-08 gacha o'qituvchi
  ilovasi mezon bahosini ikki marta qo'shardi). Muzlatilganlar blokida esa ATAYIN `Σ grade`: baholash taxtasi
  muzlatilganlarni bermaydi, ya'ni mezondan yozilgan baho u yerda mezon checklarining O'RNINI bosadi.
  ⚠️ Mezondan avtomatik yozilgan jurnal bahosi (`JournalEntry.GradeFromCriteria = true`) ballga QO'SHILMAYDI —
  o'sha checklar mezonlar qismida allaqachon sanaladi (ilgari ikki marta sanalardi). Batafsil: pastdagi §LRN-1. Servis: `StudentBallService` (Application/Services):
  `ComputeAsync(db, groupIds?)` (groupIds=null → markaz bo'yicha), `SchoolAsync`, `TeacherAsync`.
  Endpointlar: `GET /api/admin/students/balls` (`ClassAnalyticsController`, DataCache `ball:students`),
  `GET /api/admin/teachers/{id}/rating`, `GET /api/teacher/rating` → `TeacherRatingDto`(rows: rank, ball
  tarkibi, guruhlar, o'rtacha, davomat). **UI:** admin o'qituvchi sahifasida "Reyting" tabi (TOP-3 podium
  + jadval), o'qituvchi ilovasida `/teacher/rating` (profil menyusidan), admin "O'quvchilar" ro'yxatida
  "Ball" ustuni + "Ball: yuqoridan/pastdan" saralash (TOP-3 ga medal).

- **YANGI (CRM 4-bosqich) — o'quvchilar REYTINGI = testlar + ball** (`.claude/rules/rating.md`): ball komponenti
  `StudentBallService.ComputeByGroupAsync(db, groupIds, from, to)` (SANA ORALIG'I varianti, oylik/umumiy bilan
  AYNAN bir formula, SQL'da). Quyidagi "yig'ilgan ball" reytinglari (o'qituvchi reytingi, eski portal endpointi)
  O'ZGARMADI. ⚠️ `RatingService.SchoolAsync` endi SQL agregatlari bilan (butun jurnal xotiraga yuklanmaydi) —
  raqamlari eskisi bilan bir xil (`RatingEngineTests`).
- **Reyting hamma joyda YIG'ILGAN BALL bo'yicha** (o'rtacha baho EMAS): `RatingService.SchoolAsync`
  `StudentBallService.ComputeAsync` dan `Ball` ni qo'shadi (`StudentRatingRowDto.Ball`,
  `PortalRatingRowDto.Ball`), o'quvchi portali guruh/markaz reytingi ball bo'yicha saralanadi va ballni
  ko'rsatadi (teng bo'lsa o'rtacha baho hal qiladi). Kesh `rating:school` deps'iga `CriterionGrade`
  qo'shilgan (ikkala chaqiruv joyida ham). O'quvchi profilida (admin) "Yig'ilgan ballar" bo'limi va
  o'quvchi portali "Baholash" ekranida "Bu oyda / Jami yig'ilgan" ball
  (`StudentGradingGroupDto.MonthBall/TotalBall`) — o'rtacha ko'rsatilmaydi.

## Guruhdan CHIQQAN o'quvchi jurnalda (2026-10-06)

`JournalService.GroupMonthAsync` faol a'zolardan tashqari guruhdan **chiqqanlarni** ham beradi — faqat
ular O'QIGAN oylarda (boshlangan oyidan chiqish oyigacha). Bunday qator `Status="frozen"` bilan keladi
(klient uni muzlatilganlar blokida FAQAT KO'RISH qiladi; o'qituvchi jurnali va bulk davomat muzlatilganlarni
umuman olmaydi), `FrozenAt` — oxirgi kuni, `Ended` — `"left"` (chiqarilgan/boshqa guruhga o'tgan) yoki
`"completed"` (sertifikat bilan). Yorliq «Chiqgan»/«Tugatgan», «Muzlatilgan» EMAS.
⚠️ Ilgari faqat `IsActive` olinardi: chiqarilgan/o'tkazilgan o'quvchi o'qigan oylaridan ham yo'qolar,
sertifikat bilan tugatilgan guruh jurnali bo'm-bo'sh ochilardi.

## 2026-10-07 AUDIT — yozish darvozasi, davomat xabarlari, mezon bahosi, ko'chirish

### A'zolik darvozasi — `JournalService.MemberWriteError` (LRN-3)

Jurnalga (katak, ommaviy davomat, baholash mezoni) faqat SHU guruh a'zosiga yoziladi:

| Kim | Kimga |
|---|---|
| O'qituvchi (`teacher: true`) | faqat FAOL, MUZLATILMAGAN, ARXIVLANMAGAN a'zo, a'zolik boshlangan kundan |
| Admin | muzlatilgan/chiqqan o'quvchini ham tuzata oladi, lekin faqat u guruhda BO'LGAN kunga (`StudentJournalBuilder.BoundsOf`: muzlatish/chiqish sanasigacha). Tugash sanasi noma'lum yopiq a'zolik — rad |

⚠️ Ilgari "a'zolik topilmadi" = "cheklov yo'q" deb talqin qilinardi: API orqali begona o'quvchiga baho qo'yib,
ommaviy "hammasi kelmadi"da esa uning ota-onasiga SMS yuborib bo'lardi. Ommaviy amalda bunday id'lar JIMGINA
chetlab o'tiladi (`BulkAttendanceResult.SkippedIds`) va ularga xabar ketmaydi. Baholash mezoni (`Done=true`) —
o'qituvchi qoidasi bilan (muzlatilgan o'quvchi baholanmaydi), olib tashlash a'zolikka qaramaydi.

### Davomat avto-xabarlari — `AttendanceNotify` (C8)

- **"Kech" — kelmadi EMAS.** `IsLate` sabab hech qachon "darsga kelmadi" yubormaydi (o'quvchi darsda bo'lgan;
  davomat foizida ham "kelgan"). "Keldi" xabari ham ketmaydi — u faqat aniq `Present` belgisiga bog'langan.
- **Faqat O'TISHDA:** "kelmadi emas" (belgisiz yoki "kech") → "kelmadi". Sababni almashtirish (Sababsiz → Kasal)
  yangi hodisa emas — ikkinchi SMS ketmaydi.
- **Ommaviy** — faqat shu so'rovda YANGI kelmaganlarga (`NewlyAbsentIds`) va FONDA. Sabab: 25 ta SMS
  bittalab ketsa so'rov Cloudflare'ning 100 soniyasidan oshar, o'qituvchi qayta bosar va hamma ota-ona xabarni
  ikki marta olardi. ~~Bitta katak — so'rov ichida~~ — 2026-10-12 dan bitta katak HAM navbat orqali (pastdagi
  «KELMADI XABARLARI NAVBATI», CRM U3b).
- ⚠️ **Ommaviy xabarlar navbati BAZADA (2026-10-08)** — `AttendanceNotifyItem` (har oluvchi — bitta qator).
  Ilgari fon ishi xotirada edi (`Task.Run`): yuborish paytida deploy bo'lsa qolgan ota-onalar xabarni JIMGINA
  olmasdi. Endi `AttendanceNotify.EnqueueBulkAsync` qatorlarni `JournalService.BulkAttendanceAsync` ning
  `beforeSave` chaqiruvida qo'shadi — jurnal yozuvi bilan BITTA SaveChanges (jurnal saqlandi = xabar navbatda).
  Ishchi — `AttendanceNotifyWorker` (singleton + fon xizmati, controller saqlagach `Wake()`, aks holda har
  daqiqada o'zi tekshiradi): har oluvchi yuborishdan OLDIN `sending` deb band qilinadi (concurrency token),
  restartda qolgan `pending` lar yuboriladi, aynan yuborish paytida uzilgani `unknown` (QAYTA yuborilmaydi —
  eski ommaviy SMS navbati bilan bir xil qoida). 6 soatdan eski qator yuborilmaydi (`expired` — kechagi "kelmadi"
  ertasiga ketmasin; 2026-10-12 dan 6 soat `SendAfter` dan sanaladi — U3b). Qoida (trigger) yoqilmagan bo'lsa qator umuman yozilmaydi. Arxiv va joriy «keldi»
  holati YUBORISH paytida qayta tekshiriladi. Yakunlangan qatorlar 7 kundan keyin tozalanadi.
- **Bitta hodisa — bitta xabar:** standart "Davomat" kabinet bildirishnomasi faqat `attendance_absent` qoidasi
  UMUMAN yo'q bo'lsa (baho bildirishnomasi bilan bir xil naqsh); qoida bor bo'lsa xabarni faqat dispatcher
  yuboradi. (2026-10: push olib tashlandi — standart xabar endi faqat kabinet qo'ng'iroqchasiga yoziladi.)
- Arxivlangan o'quvchiga (eski yozuvlarda a'zoligi tirik bo'lishi mumkin) hech qanday davomat/baho xabari ketmaydi.

### Arxivlangan o'quvchi jurnalda (C1)

Eski arxivdagilarda a'zolik hali TIRIK bo'lishi mumkin. Shuning uchun `GroupMonthAsync` JORIY oyda arxivlangan
o'quvchining tirik a'zoligini ko'rsatmaydi (o'qituvchi uni belgilashga majbur bo'lmasin, tezkor davomat to'xtab
qolmasin, ota-onaga SMS ketmasin). O'TGAN oylarda — o'qigan joyida ko'rinadi.

### Qarz summasi o'qituvchiga berilmaydi (AUTH-8)

`GroupJournalStudentDto.Debtor` (qarzdormi) + `DebtMonths`. O'qituvchi `journal/group` javobida `Balance` HAR
DOIM 0 (o'rinbosarga ham) — teacher-portal.md §3, substitute-teachers.md §8. Admin jurnali summani ko'radi.

### LRN-1 — MEZON BAHOSINING EGASI (`JournalEntry.GradeFromCriteria`)

"Mezon checklari soni = jurnal bahosi" sinxroni (`GradingService.SyncJournalGradeAsync`) FAQAT o'z bahosiga tegadi:

- katakda QO'LDA qo'yilgan baho bo'lsa — sinxron hech narsa qilmaydi (ilgari qo'lda 5 bitta check bilan 1 ga
  aylanar, ommaviy "olib tashlash" kunning barcha qo'lda bahosini o'chirardi);
- checklar bor — baho = son, `GradeFromCriteria = true`; checklar yo'q — o'z bahosi tozalanadi.
- Katakda baho O'ZGARTIRILSA — u qo'lda bahoga aylanadi. Baho o'zgarmasa (faqat uy vazifa tahrirlandi) egasi
  saqlanadi — aks holda ballda ikki marta sanalardi.
- Mezon o'chirilsa / guruhdan ajratilsa — ta'sirlangan kataklar qayta sinxronlanadi (`ResyncAsync`, darsni
  "o'tildi" qilmaydi), `ExecuteDeleteAsync` dan keyin kesh qo'lda yangilanadi.

### LRN-4 — baholash taxtasi jurnal qoidalariga bo'ysunadi

`GradingService.CheckGradableDateAsync`: kelajak sana — taqiq, guruh boshlanishidan oldin — taqiq, sana guruhning
HAQIQIY dars kuni bo'lishi shart, `JournalPolicy.CheckAsync` (o'qituvchiga har doim, adminga "Adminlarga ham
qo'llash" bo'yicha). O'qituvchi yo'lida: `journal` ruxsati shart, to'lov darvozasi yashirgan o'quvchi taxtada
ko'rinmaydi, unga mezon qo'yib bo'lmaydi, ommaviy belgilashda chetlab o'tiladi. Sabab: mezon belgisi darsni
"o'tildi" qiladi — bu MAOSH (`SalaryJournalStats`), ya'ni jurnalda yopiq kunni taxta orqali ochib bo'lmasin.

### LRN-2 — darsni ko'chirish (`JournalService.CheckMoveAsync`)

Ko'chirish oyning dars sonini va jurnal jarimasini o'zgartiradi (asl kun rejadan chiqadi), shuning uchun:

- **o'rinbosar** qamragan kun hech kimga ko'chirilmaydi — avval tayinlovni o'zgartirish kerak (tayinlov SANAGA
  bog'langan: haq va kirish huquqi dars yo'q kunda qolib ketardi);
- jurnal siyosati ikkala sanaga ham (o'tgan bo'lsa);
- **o'qituvchi**: o'tgan darsni KELAJAKKA ko'chira olmaydi; `SalaryRequireJournal` yoqiq bo'lsa maosh muhlati
  (`SalaryGraceDays`) o'tgan darsni ham ko'chira olmaydi (ilgari dushanbadagi belgilanmagan darsni keyingi haftaga
  ko'chirib jarimadan qutulish mumkin edi). Admin — mumkin ("qo'shimcha dars" uning qarori), lekin tarixga yoziladi;
- bekor qilish ham shu qoidalarga bo'ysunadi; har amal `audit` (`Group`, guruh "Tarix" tabida);
- mezon belgilari (`CriterionGrade`) ham yangi kunga ko'chadi (ilgari eski kunda qolib ketardi).

### Boshqalar

- **"Jurnal boshqaruvi" saqlanganda tarixga yoziladi** (`CenterMeta`, Sozlamalar bo'limi): maoshni jurnalga
  bog'lash, muhlat, to'lamaganlarni yashirish — PULGA ta'sir qiluvchi sozlamalar (`SettingsController.LogSettings`
  shakli: o'zgargan maydonlar nomi + oldin/keyin).
- **Kunlik davomat sahifasi** (`StudentAttendanceController.Absent`) "kelganlar"ni faqat shu kuni darsda
  KUTILGAN a'zolardan sanaydi (`StudentJournalBuilder.ExpectedOn`): muzlatilgan (muzlatish sanasidan keyin),
  hali qo'shilmagan va arxivlangan o'quvchi "kelgan" bo'lib chiqmaydi.


## 2026-10-12 — YAGONA DAVOMAT TA'RIFI (CRM Phase 3, U3a)

SPEC: `docs/SPEC-crm-upgrade.md` P3.1. Ilgari "kech" va "belgilanmagan" har ekranda boshqacha sanalardi
(kunlik sahifa kech kelganni KELGAN, o'quvchi jurnali esa bahosiz kechni "kelmagan" der, guruh sahifasi
"o'tildi + belgisiz"ni KELDI, jurnal esa "davomat olingan + belgisiz"ni keldi derdi).

### Qoida — `AttendanceRules.Classify` (Application/Services/Attendance, sof, `AttendanceRulesTests`)

Tartib: bloklangan → sabab (`IsLate` → **Late**, `IsExcused` → AbsentExcused, aks holda AbsentUnexcused) →
aniq «keldi» / baho / o'zlashtirish → **Present** → dars o'tilmagan → NotHeld → davomat TO'LIQ olingan va
`PresentDefaultFrom` dan keyin → Present → aks holda **Unmarked**.

`Tally`: o'tilgan = Present + Late + AbsentExcused + AbsentUnexcused; kelgan = Present + Late;
foiz = round(kelgan × 100 / o'tilgan). **Unmarked / NotHeld / Blocked foizga KIRMAYDI.**

- ⚠️ **Kech = KELGAN** hamma joyda (profil, kabinet, o'qituvchi ro'yxati, guruh tabi, AI kirishi).
- ⚠️ **"O'tildi" + belgisiz ≠ keldi.** Bitta baho darsni "o'tildi" qiladi, lekin davomat OLMAYDI — qolgan
  o'quvchilar BELGILANMAGAN (foiz ham, avto-"keldi" ham yo'q). Bu o'zgarish orqaga ham ta'sir qiladi
  (foizlar biroz o'zgaradi) — egasi qabul qilgan (`ASSUMPTIONS.md` 2026-10-11).
- Klient nusxasi — `lib/attendance.ts` (`attendanceMark`, `attendanceTally`) va `classes/detail/shared.ts`
  `autoPresent` (endi `attendanceTaken` oladi, `conducted` EMAS). Server qoidasi o'zgarsa — ikkalasini ham.
- Qo'llangan joylar: `StudentJournalBuilder` (katakda `Mark`), `TeacherHomeService.Attendance`,
  `StudentProfileBuilder`, `Analytics.BuildClass` (+ a'zolik chegaralari), `GroupSnapshotBuilder` (AI),
  `StudentAttendanceController.Absent` (davomat OLINGAN guruhlar, `IsExcused`), admin/o'qituvchi guruh
  sahifalari. `RatingService`/`StudentBallService` — Phase 4 (U4b) qayta yozadi, bu yerda TEGILMAGAN.
  ⚠️ Yangi ko'rsatkich yozsangiz `IsLate`/`ReasonId != null` ni qo'lda sanamang — `Classify`/`Tally`.

### "Davomat olindi" — IKKI XIL savol (`AttendanceTaking`)

| Ustun | Ma'nosi | Kim qo'yadi | Kim o'qiydi |
|---|---|---|---|
| `LessonNote.AttendanceTakenAt` (+ `AttendanceTakenById`) | BIRINCHI davomat belgisi («keldi» yoki sabab) vaqti | katak (`SetEntryAsync`) va ommaviy davomat; faqat BIR MARTA | eslatma (`LessonAttendanceReminderService`), monitor (`DashboardController.today-lessons`), kunlik sahifa, jarima (U3c), xabarlar navbati (U3b) |
| `LessonNote.AttendanceTaken` | davomat TO'LIQ | ommaviy davomat YOKI katak saqlangach shu darsda KUTILGAN har o'quvchida «keldi»/sabab/baho bor | avto-"keldi" (`Classify`), `GroupJournalDto.AttendanceTakenDates` |

- ⚠️ Baho yolg'iz `AttendanceTakenAt` ni QO'YMAYDI (lekin to'liqlikka kiradi — o'quvchi darsda bo'lgan).
- "Kutilgan" = `StudentJournalBuilder.ExpectedOn` + arxivlanmagan; O'QITUVCHI yo'lida to'lov darvozasi
  yashirgan o'quvchi kutilmaydi (u uni ko'rmaydi).
- Eslatma/jarima/monitor uchun "olindi" = guruhning shu KUNDAGI istalgan `LessonNote` ida `AttendanceTakenAt`
  bor (fan/dars raqamidan qat'i nazar). Bitta-bitta belgilaydigan o'qituvchi shu sababli jarima olmaydi.
- Migratsiyada oxirgi 60 kun uchun `AttendanceTakenAt` = dars SANASI 00:00 (o'z vaqtida) bilan to'ldiriladi
  (`docs/migrations/Phase3.sql`) — deploy jarima to'lqini bermaydi, monitor tarixi ham to'g'ri.

### «Sababli (uzrli)» — `AbsenceReason.IsExcused`

Sozlamalar → O'quv → Sabablar → «Davomat sabablari» da «Sababli (uzrli)» belgisi. `IsLate && IsExcused` —
400 (`AbsenceReasonRules.ExcusedError`). Ishlatilgan sababda ham o'zgartiriladi (faqat analitika: ketma-ket
kelmaslik seriyasi, sababsizlar sanog'i); `IsLate` esa ishlatilgan sababda hamon O'ZGARMAYDI. Migratsiya
backfill: kech emas va nomida «sababsiz» yo'q → sababli. Yangi bazada «Kasal», «Sababli» — sababli.

### Davomat sozlamalari — `AttendanceSettingsController` (`api/admin/attendance/settings`, `[AdminPerm("classes")]`)

`CenterMeta`: `AttendanceDisciplineEnabled`, `AttendanceDeadlineMinutes` (5..120, 15), `AttendanceReminderMinutes`
("5,10", ≤ 3, o'sib boruvchi, 1..muddat), `AttendanceEscalateEnabled`, `AttendanceFineEnabled`,
`AttendanceFineAmount` (0..10 mln, yoqilganda > 0), `AbsentNoticeDelayMinutes` (0..180), `AbsentNoticeApproval`
(`auto`/`manual`/`late_only`). Tekshiruv — `AttendanceSettingsRules.Validate` (sof). Saqlash tarixga QIYMATLARI
bilan (`SettingsAudit`, "Davomat intizomi"). Eski «Davomat eslatmasi» avto-qoidasi yoqiq bo'lsa javobda
ogohlantirish (ikki marta eslatma). UI: «Jurnal va davomat» → «Davomat intizomi» (`AttendanceDisciplineModal`)
va «Davomat xabarlari» (`AbsentNoticeSettingsModal`; navbat — U3b, pastda).

## 2026-10-12 — «KELMADI» XABARLARI NAVBATI va MENEJER NAZORATI (CRM Phase 3, U3b)

SPEC: `docs/SPEC-crm-upgrade.md` P3.3. Ilgari «kelmadi» belgisi qo'yilishi bilan ota-onaga ketardi — noto'g'ri
belgilangan (keyin tuzatilgan) o'quvchining ota-onasi ham xabar olardi. Endi xabar DARS TUGAGACH ketadi va
undan oldin menejer uni to'xtata oladi. Migratsiya (integrator): `CrmAbsentNotices` + `docs/migrations/Phase3b.sql`.

### Qachon ketadi — `AbsentNoticeRules` (sof, `AbsentNoticeTests`)

| | «Kelmadi» (`absent`) | «Keldi» (`present`) |
|---|---|---|
| `SendAfter` | dars sanasi + dars TUGASHI + `AbsentNoticeDelayMinutes` (tugash noma'lum → belgi + kechikish; hech qachon belgidan oldin) | belgi vaqti — DARHOL (SPEC §10 #4) |
| Tasdiq | `AbsentNoticeApproval`: `auto` → `pending`; `manual` → `awaiting`; `late_only` (standart) → davomat KECH olingan bo'lsa `awaiting`, aks holda `pending` | hech qachon |

- **Dars vaqti** — `AttendanceNotify.LessonContextAsync`: `AttendanceWatch.LessonsOn` (ko'chirilgan dars yangi
  kunida/vaqtida), kunning BIRINCHI darsi; shu kuni rejada dars yo'q (qo'shimcha dars) → guruhning odatiy vaqti.
- **Kech olingan** (`TakenLate`) = kunning BIRINCHI davomat belgisi (`AttendanceTakenAt`, guruh bo'yicha) > dars
  boshlanishi + `AttendanceDeadlineMinutes`. Boshlanish noma'lum — hech qachon kech emas.
- ⚠️ **Faqat BUGUNGI dars** navbatga tushadi — o'tgan kunni tuzatish ota-onaga xabar BERMAYDI («keldi» ham).
- ⚠️ **Bitta katak ham navbat orqali** (`AttendanceNotify.EnqueueSingleAsync`, admin `JournalController.SetEntry` va
  o'qituvchi `TeacherPortalController` PUT) — so'rov ichida hech narsa yuborilmaydi. Ommaviy — `EnqueueBulkAsync`
  (jurnal bilan bitta SaveChanges, avvalgidek).
- Kabinet «Davomat» standart bildirishnomasi (qoida UMUMAN yo'q bo'lsa) o'zgarmadi — u ota-onaga Telegram emas.

### Holatlar (`AttendanceNotifyItem.Status`, concurrency token)

`pending` (SendAfter da o'zi ketadi) · `awaiting` (tasdiq kutadi) · `held` (menejer to'xtatgan) · `approved` ·
`sending` · `done` · `failed` · `unknown` · `expired` · `cancelled` (`CancelReason`: `manager` · `changed` · `archived`).

| Amal (`CanApply`) | Qaysi holatdan |
|---|---|
| To'xtatish (`hold`) | pending · awaiting · approved |
| Tasdiqlash (`approve`) | pending · awaiting · held |
| Bekor qilish (`cancel`) | pending · awaiting · held · approved |
| boshqasi | 409 «Xabar allaqachon yuborilgan yoki yopilgan» |

- ⚠️ **HECH KIM TEGMASA:** `pending` — SendAfter da ketadi; `awaiting`/`held` — HECH QACHON ketmaydi va
  `SendAfter + 6 soat` da `expired` («Muddati o'tdi»). «Tasdiqlash» xabarni ERTA yubormaydi — u SendAfter da
  (o'tib ketgan bo'lsa darhol) ketadi.
- ⚠️ **Jurnal o'zgarsa — BEKOR** (`AttendanceNotify.ReconcileAsync`): "kelmadi" → «keldi»/«kech»/tozalandi
  (yoki «keldi» → sabab) bo'lsa ochiq xabar `cancelled` (`changed`); o'quvchi arxivlansa — `archived`. Har jurnal
  yozuvidan keyin (katak, tozalash, ommaviy) VA ishchining HAR aylanishida (boshqa yo'llar — ko'chirish va h.k.)
  hamda yuborishdan oldin yana bir bor. Sabab almashishi (Sababsiz → Kasal) yangi hodisa EMAS — bitta xabar,
  matnda JORIY sabab.
- Ishchi (`AttendanceNotifyWorker`): faqat `(pending|approved) && SendAfter <= now`; band qilish `sending`
  (concurrency) — menejerning «To'xtatish»i bilan poyga shu token bilan hal bo'ladi (409 «Holat o'zgardi»).
  Yuborish — `AutoMessageService` → outbox, `eventKey = sourceId = item.Id` (`messaging.md` §0.5).
- **Menejerlarga ogohlantirish:** dars uchun `awaiting` xabarlar paydo bo'lsa — superadmin, admin va
  `students.notices` ruxsatli xodimga BIR MARTA (`LessonReminderMark` kaliti `notice-await:{groupId}:{sana}`):
  qo'ng'iroqcha + Telegram (`TelegramRegistration.UserId`, ishchi xabar — outbox emas): «🕓 {guruh} ({sana}) — {n} ta
  «kelmadi» xabari tasdiq kutmoqda: davomat kech olingan ({vaqt}). Tekshiring: https://{App:Host}/admin/nazorat/
  davomat-xabarlari?date={sana}» (`App:Host` bo'sh bo'lsa havolasiz; `manual` rejimda sabab — «har bir xabarni
  menejer tasdiqlaydi»).

### Nazorat sahifasi — Nazorat → «Davomat xabarlari»

`/admin/nazorat/davomat-xabarlari` (`AbsentNoticesPage`), ruxsat **`students.notices`** («Davomat xabarlari
nazorati», O'QISH ham — `ReadRequiresPerm`). API `api/admin/attendance-notices`: `GET ?date&status&groupId&teacherId`
(sanoqlar status filtridan OLDIN; darslar kesimida; yetkazish — `MessageDelivery.SourceId`), `GET summary`
(bosh sahifa vidjeti), `PUT {id}/hold|approve|cancel { note? }`, `PUT bulk { ids, action, note? }` (≤ 500;
yopilganlari `skipped`; parallel o'zgarish — hech narsa saqlanmaydi, 409). Har amal — audit `AttendanceNotice`
(«Guruhlar», `EntityId = "{groupId}:{itemId}"`, `studentId`): ««Kelmadi» xabari to'xtatildi / tasdiqlandi /
bekor qilindi: {o'quvchi}, {guruh}, {sana}» (+ izoh). Sahifa bugun ko'rilsa har 30 s yangilanadi; `?date=` bilan
ochiladi (Telegram havolasi). Klient nusxasi — `lib/absentNotices.ts` (o'tishlar jadvali va yorliqlar serverdagi
bilan BIR XIL — `absentNotices.test.ts` server manbasini o'qib qulflaydi).
