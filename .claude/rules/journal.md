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

- **BALL (reyting bali)** = `Σ(jurnal baholari) + Σ(bajarilgan baholash mezonlari)` — guruh sahifasidagi
  "Reyting" tabi bilan BIR XIL formula. Servis: `StudentBallService` (Application/Services):
  `ComputeAsync(db, groupIds?)` (groupIds=null → markaz bo'yicha), `SchoolAsync`, `TeacherAsync`.
  Endpointlar: `GET /api/admin/students/balls` (`ClassAnalyticsController`, DataCache `ball:students`),
  `GET /api/admin/teachers/{id}/rating`, `GET /api/teacher/rating` → `TeacherRatingDto`(rows: rank, ball
  tarkibi, guruhlar, o'rtacha, davomat). **UI:** admin o'qituvchi sahifasida "Reyting" tabi (TOP-3 podium
  + jadval), o'qituvchi ilovasida `/teacher/rating` (profil menyusidan), admin "O'quvchilar" ro'yxatida
  "Ball" ustuni + "Ball: yuqoridan/pastdan" saralash (TOP-3 ga medal).

- **Reyting hamma joyda YIG'ILGAN BALL bo'yicha** (o'rtacha baho EMAS): `RatingService.SchoolAsync`
  `StudentBallService.ComputeAsync` dan `Ball` ni qo'shadi (`StudentRatingRowDto.Ball`,
  `PortalRatingRowDto.Ball`), o'quvchi portali guruh/markaz reytingi ball bo'yicha saralanadi va ballni
  ko'rsatadi (teng bo'lsa o'rtacha baho hal qiladi). Kesh `rating:school` deps'iga `CriterionGrade`
  qo'shilgan (ikkala chaqiruv joyida ham). O'quvchi profilida (admin) "Yig'ilgan ballar" bo'limi va
  o'quvchi portali "Baholash" ekranida "Bu oyda / Jami yig'ilgan" ball
  (`StudentGradingGroupDto.MonthBall/TotalBall`) — o'rtacha ko'rsatilmaydi.
