---
description: Moliya — a'zolik holati (trial/active/frozen), per-guruh hisob va balans, o'qituvchi maoshi va maoshning jurnalga bog'lanishi.
paths:
  - "WunderkindLC.Application/Services/Tuition*.cs"
  - "WunderkindLC.Application/Services/Salary*.cs"
  - "WunderkindLC.Application/Services/TeacherSalaryCalc.cs"
  - "WunderkindLC.Application/Services/GroupBalanceService.cs"
  - "WunderkindLC.Application/Services/StudentGroupLedger.cs"
  - "WunderkindLC.Application/Services/StudentLedger.cs"
  - "WunderkindLC.Application/Services/CourseFinanceReport.cs"
  - "WunderkindLC.Application/Services/MembershipLifecycle.cs"
  - "WunderkindLC.Server/Controllers/FinanceController.cs"
  - "WunderkindLC.Server/Controllers/TeachersController.cs"
  - "WunderkindLC.Client/src/pages/admin/finance/**"
---

# Moliya / billing qoidalari

- **MAOSH QAYSI OYGA TEGISHLI — `FinanceTransaction.Month`, to'lov SANASI EMAS.** Iyul maoshi
  5-avgustda berilishi mumkin: `Date` = pul berilgan kun, `Month` ("yyyy-MM") = qaysi oy uchun.
  `SalaryLedger.BuildAsync` to'lovlarni `Month` bo'yicha guruhlaydi; `Month` bo'sh/buzuq bo'lsa
  (eski yozuvlar) — orqaga moslik uchun `Date`dan olinadi (`PayMonth` yordamchisi). **So'rov
  sana oralig'i bilan filtrlanMAYDI** — aks holda kech berilgan to'lov (masalan iyul maoshi
  sentyabrda) oraliqdan tushib qolib, umuman ko'rinmasdi; filtr oy bo'yicha keyin qo'llanadi.
  UI: Moliya → "Yangi amal" (maosh chiqimi) va o'qituvchi profili → Maosh → "To'lov qilish" —
  ikkalasida ham **"Qaysi oy uchun"** va **"Sana (berilgan kun)"** ALOHIDA maydonlar.
  ⚠️ Ilgari Moliya formasi maoshda `Month`ni umuman saqlamasdi (`month: isTuitionIncome ? ... : undefined`)
  va backend sanadan olardi — oylar aralashib ketardi. Eski (Month'siz) yozuvlar avvalgidek
  sana bo'yicha hisoblanaveradi, o'z-o'zidan tuzalmaydi — kerak bo'lsa qo'lda tahrirlanadi.

- **Maosh QO'LDA, 2 rejim** (`Teacher.SalaryMode`): **"fixed"** — admin `Teacher.Salary` qat'iy summasini
  kiritadi; **"percent"** — o'qituvchi guruh(lar)idan **SHU OY UCHUN** yig'ilgan tuition to'lovining
  `Teacher.SalaryPercent` foizi. Hisob: `SalaryLedger.CollectedPerGroupAsync` — guruh = o'sha OYDA
  guruhni o'qitgan o'qituvchi (tarix bo'yicha, darslar ulushida — pastda «O'TGAN OY KIMNIKI»); to'lov `FinanceTransaction.GroupId` tegiga ega bo'lsa 100% o'sha guruhga,
  **teglanmagan** to'lov esa o'quvchining shu oydagi billable guruhlari `MonthlyFee` nisbatida
  taqsimlanadi. Frontend `TeacherSalaryPage` (rejim toggle + foiz).

- ⚠️ **FOIZ BAZASI O'ZGARDI (2026-09-25):** `CenterMeta.SalaryChargedBaseFrom` ("yyyy-MM", prod'da
  `2026-09`) dan boshlab foiz **HISOBLANGAN TO'LIQ oylikdan** (`MonthlyCharge.Amount`, chegirma
  AYRILMAYDI — chegirmani markaz ko'taradi). Undan oldingi oylar avvalgidek **yig'ilgan** puldan
  (ular uchun maosh allaqachon berilgan — qayta hisoblanmaydi). Sozlama bo'sh = eski qoida.
  Qoida bitta joyda: `SalaryLedger.UsesChargedBase` + `PercentBases.SalaryBase`; o'rinbosarlik
  hovuzi ham AYNAN shu bazadan (`CollectedForGroupsAsync` endi `SalaryBase` ni qaytaradi — nol
  yig'indili model buzilmasin). Shu oylardan boshlab quyidagi "pul kelmaguncha maosh 0" jumlalari
  **faqat eski oylarga** tegishli. Testlar: `StaffRolesAndSalaryBaseTests`.
  ⚠️ **"HOZIR CHIQADIGAN" — `MonthSalaryDto.CollectedExpected` (2026-10-07, faqat KO'RSATKICH):**
  rasmiy maosh (`Expected`) hisoblangan bazada QOLADI (foydalanuvchi tanlovi), yonida esa "shu
  oyning TUSHGAN pulidan hozir qancha chiqadi" ko'rsatiladi: guruh bo'yicha yig'ilgan pul × foiz
  (qat'iy guruhda — qat'iy ulush) − o'rinbosar ushlanmasi + o'rinbosar haqi, `Expected` dan
  OSHMAYDI. Farqi = "qarzdorlar to'lasa yana +". Yig'ilgan bazali (eski) oylarda = `Expected`.
  Hech narsa to'lamaydi va hech qayerga yozilmaydi — o'qituvchini qarzdorlarni yopishga undash
  uchun (o'qituvchi kabineti → Maosh, admin o'qituvchi sahifasi).
- **Umumiy foiz:** o'qituvchi qo'shishda `Teacher.SalaryPercent` (Boshqaruv → Xodimlar, rol
  "O'qituvchi"). Guruh `TeacherSalaryMode = ""` bo'lsa unga ergashadi ("Umumiy"), kerak bo'lsa
  guruhda boshqa foiz/qat'iy summa (`GroupSalaryEditor`).
  ⚠️ Umumiy foiz qo'yilganda/o'zgarganda o'qituvchining **"qat'iy, 0 so'm"** guruhlari avtomatik
  "Umumiy"ga o'tadi (`TeachersController.Update`, auditga yoziladi) — ko'chirilgan (edutizim)
  guruhlar shunday turadi va aks holda foiz qo'yilsa ham maosh 0 bo'lib qolardi. Qat'iy summasi
  BOR guruh tegilmaydi.
  ⚠️ Maosh sozlamasi VERSIYALANMAYDI: foiz qo'yilsa o'tgan oylar hisobi ham shu foiz bilan
  ko'rinadi (bazasi o'sha oyning qoidasi bo'yicha). Bu avvaldan shunday — alohida ish.
  **TO'LOV QAYSI OYGA — `FinanceTransaction.Month`, to'lov SANASI EMAS** (yuqoridagi maosh to'lovlari
  bilan AYNAN bir xil qoida): o'quvchi 3-avgustda IYUL uchun to'lasa, pul o'qituvchining IYUL
  maoshiga kiradi — u iyulda dars bergan. Vozvrat ham o'z oyidan ayriladi. `Month` bo'sh bo'lgan
  ESKI yozuvlarda sana ishlatiladi; so'rov shu sabab ikki shartli (oyi mos YOKI oyi yo'q va sanasi
  oraliqda) — aks holda kech to'langan pul umuman tushib qolardi (`CourseFinanceReport
  .LoadPaymentsAsync` bilan bir xil naqsh).
  ⚠️ Ilgari FAQAT shu joyda to'lov SANASI ishlatilardi va o'qituvchi profilida bitta qatorning ikki
  yarmi turlicha hisoblanardi ("iyul uchun berilgan maosh" — `Month`, "iyulda yig'ilgan" — SANA);
  raqamlar tushunarsiz chiqardi. Endi butun tizim bitta konvensiyada.
  DIQQAT: `TeachersController.Create/Update` `Salary/SalaryMode/SalaryPercent`ni YOZADI (ilgari Salary
  umuman yozilmasdi — latent bug); `TeacherFormModal` bu maydonlarni round-trip qiladi (profil tahrirda
  reset bo'lmaydi).

## OYLIK MODEL — ABONEMENT (2026-09-20, foydalanuvchi bilan aniqlashtirilgan)

Markazning modeli: kurs narxi — **oylik abonement**. Har oyning boshida faol o'quvchiga
**to'liq oylik** qarz yoziladi (`TuitionAccrualService` → `AccrueDue`, startupda va har 12 soatda;
`AccruableMonth` aktivlashtirilgan oydan KEYINGI oylarni oladi), to'lov esa shu qarzdan ayiriladi.
O'quvchida **bitta balans** — `Students.Balance`.

⚠️ **DARS SONI ODATDA AHAMIYATSIZ.** U faqat IKKI joyda ishlatiladi va ikkalasi ham "haqiqatan
kam dars o'tilgan" holat: oy o'rtasida **aktivlashtirish** (qolgan darslar) va oy o'rtasida
**muzlatish/chiqish** (o'qilgan darslar). Ikkalasi ham bitta funksiya —
`TuitionService.ProratedLessonCharge`.

⚠️ **«12 DARS» CHEGARASI OLIB TASHLANDI.** Ilgari 12 va undan ko'p dars bo'lsa to'liq oylik
olinardi; endi qisman oy HAR DOIM "dars soni × kursning bir dars narxi" (`Subject.LessonPrice`;
kiritilmagan bo'lsa eski pro-rata). Faqat dars soni oydagi JAMI darsga teng bo'lsa — to'liq oylik.
Natija to'liq oylikdan hech qachon oshmaydi.

⚠️ Ya'ni **`Subject.LessonPrice` endi muhim**: u kiritilmagan kursda oy o'rtasida qo'shilgan
o'quvchi kunlar nisbatida hisoblanadi (dars kunlari bo'yicha emas).

- **Billing + a'zolik holati:** `StudentGroup.Status` = "trial" (sinov — to'lov YO'Q) | "active" | "frozen".
  Guruhga qo'shilganda "trial". **Aktivlashtirish** (`/members/{sid}/activate` {date}): birinchi (qisman)
  oy = (guruh `MonthlyFee` ÷ SHU OYDAGI jami dars) × shu sanadan oy oxirigacha qolgan darslar
  (`group.Days` bo'yicha) — qolgan ≤ jami bo'lgani uchun to'liqdan oshmaydi, chegirma qo'llanadi
  (`TuitionService.ChargeActivationProrateAsync`). **ORQAGA SANALGAN aktivlashtirishda** (masalan iyulda
  fevraldan) oraliq oylar — aktiv oydan KEYINGI oydan joriy oygacha — DARHOL to'liq oylik bilan yoziladi
  (`TuitionService.AccrueCatchUpAsync`, javobda `catchUpMonths`); ilgari ularni faqat fon xizmati
  (har 12 soatda) yozardi va `AccrueMonth` dagi `EnrollmentDate` filtri tufayli umuman yozilmasdi.
  **Muzlatish** (`/members/{sid}/freeze` {date}): shu
  oydan boshlab hisoblanmaydi; **muzlatish SANASINING O'ZI hisobga kiradi** (o'sha kuni dars bo'lsa
  to'lovga qo'shiladi) — `StudentGroupLedger.FreezeGross` previewi ham aynan shu konvensiyada va
  `TuitionService.ProratedLessonCharge` ni ishlatadi.
  **ORQAGA SANALGAN muzlatishda muzlatish oyidan KEYINGI hisoblar BEKOR qilinadi**
  (`TuitionService.PurgeChargesAfterMonthAsync`, javobda `restored`) — o'quvchi o'qimagan oylar uchun
  qarzdor bo'lib qolmasin; `Locked` (qo'lda tahrirlangan) qatorlar tegilmaydi — ⚠️ qulfi FAQAT kelmagan
  darslar chegirmasidan bo'lgan qator bundan MUSTASNO: u qisqartiriladi/bekor qilinadi (`discounts.md`
  §13, 2026-10-07). Bu AKTIVLASHTIRISHNING teskarisi va uchala
  yo'lda ham bir xil: **muzlatish**, **guruh almashtirish** (`transfer`) va **guruhni yopish** (`close`).
  Muzlatish sanasi AKTIVLASHTIRISH sanasidan oldin bo'lsa (umuman o'qimagan) — qisman to'lov ham
  yozilmaydi va aktivlashtirish oyi hisobi ham bekor qilinadi (`inclusive: true`).
  DIQQAT (`transfer`): bekor qilingan oylar ro'yxati `CarryGroupAdvanceAsync` ga `zeroOwedMonths`
  sifatida uzatiladi — EF hali flush qilinmagan (o'chirishga belgilangan) qatorni so'rovda baribir
  qaytargani uchun, usiz o'sha oylar "hisoblangan" bo'lib ko'rinib, ularga to'langan pul eski guruhda
  qolib ketardi. `AccrueMonth` har oy = FAOL a'zoliklarning
  `MonthlyFee` yig'indisi — faqat Status=="active", aktivlashtirilgan oydan KEYINGI oylar, muzlatish
  oyidan OLDIN (a'zoligi yo'q o'quvchi — eski ClassName narxi). **`MonthlyCharge` PER-GURUH** — unikal
  kalit (StudentId, **GroupId**, Month); guruhsiz (eski ClassName) o'quvchida GroupId=null. To'lov
  (`FinanceTransaction`) ham guruhga teglanadi (`AddPayment`: 2+ guruh bo'lsa guruh MAJBURIY, bitta
  bo'lsa avtomatik).

- **KVITANSIYA RAQAMI va TO'LOV VAQTI** (migratsiya `AddPaymentReceiptAndTime`): `FinanceTransaction`da
  `ReceiptNo` (qog'oz kvitansiya, **naqd** to'lovda kassir kiritadi — seriya "KV" + raqam) va `PaidTime`
  ("HH:mm", **karta** to'lovida pul o'tkazilgan haqiqiy vaqt). Normalizatsiya BITTA joyda —
  `PaymentFields.NormalizeReceiptNo` ("kv-123" → "KV123") / `TryNormalizeTime`; ikkala yozish yo'li
  (`StudentsController.AddPayment` va `FinanceController.Create/Update/EditPayment`) shuni ishlatadi.
  UI: `PaymentModal` usulga qarab maydon ko'rsatadi (naqd → KV prefiksli input, karta → vaqt);
  Moliya → To'lovlar jadvalida "Kvitansiya" ustuni + qidiruv ("kv123" ham, "123" ham topadi) + CSV;
  `PaymentEditModal`da tuzatish mumkin; chekda (`receipt.ts`) "Kvitansiya" qatori chiqadi.
  DIQQAT: chekdagi `ReceiptNo` (tranzaksiya Id'sidan hosil bo'ladigan ichki raqam) va qog'oz
  kvitansiya raqami (`KvNo`) — BOSHQA-BOSHQA maydonlar.
  **O'QUVCHI PROFILIDA HAM KO'RINADI:** `PaymentDto` ga `ReceiptNo`/`PaidTime`/`CardLast4`
  qo'shilgan (`StudentLedger`) — "Amalga oshirilgan to'lovlar" ro'yxatida naqd to'lovda **kun +
  KV raqami**, kartada **kun + vaqt + `•••• 1234`** chiqadi. Ilgari bu ma'lumot faqat Moliya
  bo'limida bor edi va o'quvchi kartochkasidan "qaysi kartadan, qachon to'landi" ko'rinmasdi.
  Shu DTO o'qituvchi maosh to'lovlarida ham ishlatiladi (u yerda maydonlar bo'sh).

- **KARTA RAQAMINING OXIRGI 4 RAQAMI** (migratsiya `AddPaymentCardLast4`): `FinanceTransaction.CardLast4`
  — KARTA to'lovida kassir kiritadi (bank ko'chirmasi bilan solishtirish uchun), to'lov vaqtidan
  OLDIN so'raladi. Normalizatsiya `PaymentFields.TryNormalizeCardLast4`: faqat raqamlar qoldiriladi
  va OXIRGI 4 tasi olinadi — **to'liq karta raqami hech qachon saqlanmaydi** (kassir butun raqamni
  yopishtirsa ham). Raqamlari 4 tadan kam bo'lsa 400. UI'da ham `slice(-4)` bilan cheklangan.
  KO'RINISHI: Moliya → To'lovlar jadvalidagi **"Kvitansiya" ustuni** ikki maqsadli — naqdda
  kvitansiya raqami ("KV000123"), kartada `•••• 1234` (`receiptCell()`). Shunga mos ravishda
  "Kvitansiya raqami: bor / yo'q" filtri ikkalasini ham hisobga oladi, qidiruv karta oxirgi 4
  raqami bo'yicha ham topadi, CSV'da `**** 1234` chiqadi. SARALASH esa faqat kvitansiya raqami
  bo'yicha (karta oxiri tasodifiy son — saralash ma'nosiz), kartalar ro'yxat oxirida qoladi.

- **KVITANSIYA RAQAMI BIR MARTA ISHLATILADI** (`ReceiptGuard.FindDuplicateAsync`): bitta qog'oz
  blank bo'yicha ikki marta to'lov yozilmasin. BARCHA yozish yo'llari tekshiradi —
  `StudentsController.AddPayment`, `FinanceController.Create/Update/UpdatePayment` (tahrirda
  `excludeTxId` bilan o'zini hisobga olmaydi). Band bo'lsa **409 Conflict** +
  `{ message, duplicate: DuplicateReceiptDto }` (F.I.Sh · guruh/kurs · o'qituvchi · summa · oy ·
  sana/usul · kim va qachon kiritgan). Klientda `receiptDuplicateOf(err)` shu ma'lumotni ajratadi va
  `PaymentModal`/`PaymentEditModal` ichida OGOHLANTIRISH KARTOCHKASI chiqaradi: "Bekor qilish" yoki
  **"Baribir saqlash"** (`ForceReceipt=true` — haqiqatan takroriy blank uchun; auditga
  "[takroriy kvitansiya KV… — ataylab saqlandi]" izohi tushadi).
  DIQQAT: to'lov modallari endi xatoni YUTMAYDI — `onSubmit` xatoni qayta otadi va modal faqat
  saqlash muvaffaqiyatli bo'lgandagina yopiladi (ilgari `StudentsPage` `addPayment`ni `await`
  qilmasdan modalni yopib yuborardi — xato bilinmay qolardi).

- **KASSA — TELEFON uchun alohida portal** (`/kassa`, ruxsat kaliti **`kassa`**): kassirda faqat telefon
  bor, shuning uchun admin paneli EMAS — `KassaMobileLayout` (bosh sahifa/yon menyu YO'Q, pastda 2 tab:
  "To'lov" va "To'lovlarim"). FAQAT `kassa` ruxsatiga ega xodim login'dan keyin shu yerga tushadi va
  `/admin/*` ga kirsa `/kassa` ga qaytariladi (`isKassaOnly`/`homeFor` — `config/navigation.ts`,
  `ProtectedRoute`). Admin/superadmin uchun o'sha ekran admin panelida ham bor (`/admin/kassa`).
  O'quvchini topish: (1) F.I.Sh yoki telefon qidiruvi (`GET /api/admin/kassa/students?q=`, server
  tomonda, 30 tagacha; telefon bazada `+998-XX-...` formatida saqlangani uchun raqamli moslashtirish
  XOTIRADA, kamida 4 raqam), (2) "ichiga kirish": **BARCHA** o'qituvchilar (arxivdagilar ham) → uning
  guruhlari (**ARXIV guruhlar ham** — `getClasses(true)`) → guruh o'quvchilari (chiqarilgan/muzlatilgan/
  sinov ham, balans PER-GURUH). Sabab: eski/yopilgan guruhning qarzi ham to'lanadi.
  "To'lov qilish" o'quvchilar bo'limidagi AYNAN SHU `PaymentModal`ni ochadi, saqlangach chek chiqadi.
  **YOZISH YO'LI BITTA:** `PaymentIntake.AddAsync` (Application) — `StudentsController.AddPayment`
  ham, `KassaController` (`POST /api/admin/kassa/students/{id}/payments`) ham shu xizmatni chaqiradi
  (kvitansiya nazorati, idempotentlik, avans `EnsureCharge`, audit, avto-xabar — nusxalanmagan);
  HTTP tarjimasi `PaymentIntakeHttp.ToActionResult` (409 dublikat / 400 xato / 200 `{id}`).
  NEGA ALOHIDA CONTROLLER: to'lov `[AdminPerm("students.list")]` ostida edi — kassirga to'lov uchun
  o'quvchi yaratish/tahrirlash huquqini ham berish kerak bo'lardi; endi "kassa" ruxsati YETADI
  (xodimga GET har doim ochiq, shuning uchun ro'yxatlar uchun yangi endpoint kerak emas).
  "Kassir" xodim shabloni (Program.cs seed) endi `kassa` ruxsatini ham beradi.

- **KIM QANCHA PUL QABUL QILGAN** (`CashierReport`, migratsiya `AddFinanceCreatedById`):
  `FinanceTransaction.CreatedById` — to'lovni kiritgan xodimning AKKAUNT id'si (`CreatedBy` esa
  ko'rsatish uchun F.I.Sh, chekdagi "Mas'ul"). ESKI yozuvlarda `CreatedById` null, shuning uchun
  guruhlash kaliti `CreatedById ?? "name:"+CreatedBy` — eski to'lovlar ism bo'yicha guruhlanadi va
  hisobotdan tushib qolmaydi. Faqat KIRIM (`Direction=="income"`) hisoblanadi.
  Ikki ko'rinish: **kassirning o'zi** — `GET /api/admin/kassa/my-payments?from&to` (kim ekani
  TOKENDAN olinadi, boshqa kassirnikini so'rab bo'lmaydi; standart davr — BUGUN) → "To'lovlarim"
  tabi (Bugun/7 kun/Shu oy, jami + naqd/karta/bank, qatorni bosib chekni qayta chiqarish);
  **admin/superadmin** — Moliya → **"Kassirlar"** tabi (`GET /api/admin/finance/cashiers`, qatorni
  bosish → `GET /api/admin/finance/cashier-payments`). DIQQAT: bu ikki finance endpointi ATAYIN
  qattiqroq (`CanSeeCashiers`) — odatdagi "staff'ga GET ochiq" qoidasidan farqli, kassir boshqa
  kassirlarning tushumini ko'rmaydi (faqat admin/superadmin yoki `finance` ruxsatli xodim).
  Bitta kassirni bosish — MODAL emas, **alohida sahifa** `/admin/finance/cashiers/:key`
  (`?name=&from=&to=`): ichida qidiruv (F.I.Sh · guruh · kurs · o'qituvchi · kvitansiya "kv123"/"123" ·
  karta oxiri · summa), davr, sahifalash, CSV va chek. `key` — akkaunt id'si yoki "name:F.I.Sh".
  BIRLASHTIRISH: eski (id'siz) yozuvning ismi NOYOB akkauntga to'g'ri kelsa, o'sha akkaunt id'si
  kalit bo'ladi — bitta odam ro'yxatda ikki marta chiqmaydi. Vaqtlar `AppClock.ToLocal` (UTC+5).
  Kassirning "To'lovlarim" ekrani: KUNMA-KUN (◀ sana ▶ yoki kalendardan istalgan kun) + "7 kun"/
  "Shu oy", ichida qidiruv va naqd/karta/bank filtri; jami summalar EKRANDAGI (filtrlangan) ro'yxatga
  qarab hisoblanadi.

- **KIM KIRITGANI JADVALLARDA KO'RINADI**: `FinanceTransactionDto.CreatedBy` (backend
  `FinanceController.ToDto`) — Moliya → **To'lovlar** jadvalida "Kiritgan" ustuni (qidiruv va CSV'ga
  ham kiradi) va **Umumiy → Kunlik hisobot**da kun tanlanganda "Vaqt · Kvitansiya · Kiritgan"
  ustunlari. Ya'ni har bir to'lov yonida qaysi kassir kiritgani va qog'oz kvitansiya raqami turadi.
  **"Kiritgan" FILTRI** (Moliya → To'lovlar): ro'yxat `GET /api/admin/finance/payment-authors`
  (`CashierReport.AuthorsAsync`, gate = `CanSeeCashiers`) — to'lov KIRITA OLADIGAN barcha akkauntlar
  (admin/superadmin + `kassa`/`students`/`finance` yozish ruxsatli xodim, hali to'lov kiritmagani ham)
  **+** davr ichida kiritgan, ammo endi ruxsati yo'q/o'chirilgan akkauntlar. Kalit — `CashierReport.KeyOf`
  (id yoki "name:F.I.Sh"), eski id'siz yozuv NOYOB ism bo'yicha akkauntga birlashadi. Mos kelish
  qoidasi klientda ham bir xil: `createdById == id` YOKI (id yo'q eski yozuvda) `createdBy == ism` —
  shuning uchun `FinanceTransactionDto`ga `CreatedById` qo'shilgan.

- **MOLIYA JADVALLARI SAHIFALANADI** (`components/ui/TablePagination.tsx`): `usePagination(items)`
  hook + `<TablePagination {...pg} />` — 20/30/50/100 talik, filtr/qidiruv o'zgarsa 1-sahifaga
  qaytadi. Ulangan joylar: Moliya → Amallar, O'qituvchilar, To'lovlar, Vozvratlar. CSV eksporti va
  jami summalar SAHIFAGA emas, butun FILTRLANGAN ro'yxatga tayanadi.
  To'lovlar bo'limida qo'shimcha filtrlar: **to'lov usuli** (naqd/karta/bank — chipda o'sha usul
  jami summasi) va **kvitansiya** (raqami bor / raqami yo'q).

- **ESKI / ARXIV GURUHGA TO'LOV QABUL QILINADI** (`PaymentIntake.AddAsync` → `ResolveGroup`): nomzod
  guruhlar ro'yxatida `sg.IsActive` sharti **YO'Q** — sinovdan boshqa HAMMASI **+ FAOL sinov a'zoliklari**
  (2026-10-07, quyida «SINOVDAGI O'QUVCHI TO'LOVI»). Guruhdan CHIQARILGAN,
  "tugatgan" (sertifikat bilan yopilgan guruh) va MUZLATILGAN a'zoliklarda ham qarz qolishi mumkin,
  kassir uni keyin ham qabul qila olishi shart. UI allaqachon shunday ishlaydi (`PaymentModal`
  sinovdan boshqa BARCHA a'zoliklarni ko'rsatadi, "— chiqarilgan" yorlig'i bilan; Kassa esa arxiv
  guruhlarni ataylab ro'yxatlaydi).
  ⚠️ Ilgari `sg.IsActive` talab qilinardi va IKKI xil buzilish berardi: (1) o'quvchining boshqa faol
  guruhi bo'lsa — 400 "To'lov qaysi guruh uchun ekanini tanlang" (tanlangan eski guruh ro'yxatda
  yo'q edi); (2) faol guruhi BITTA bo'lsa — to'lov JIMGINA o'sha noto'g'ri guruhga teglanardi.
  `FinanceController` (Moliya → to'lovni tahrirlash) allaqachon `IsActive`siz tekshirardi.

- **MUZLATISH HISOBI — YAGONA MANBA `MembershipBilling.SettleFreezeAsync`** (Application/Services):
  qisman to'lov (`ChargeFreezeProrateAsync`) + muzlatish oyidan keyingi hisoblarni bekor qilish
  (`PurgeChargesAfterMonthAsync`, muzlatish sanasi aktivlashtirishdan oldin bo'lsa `inclusive: true`).
  TO'RTTA yo'l ham AYNAN shuni chaqiradi: **Muzlatish** (`FreezeMember`), **Guruh almashtirish**
  (`TransferMember`), **Guruhni yopish** (`Close`) va **Guruhni tugatish — sertifikat bilan**
  (`CompleteAndTransfer`). Yangi muzlatish yo'li qo'shilsa — SHU metod chaqiriladi, nusxa ko'chirilmaydi.

- **GURUHDAN CHIQISH — YAGONA yo'l `MembershipBilling.LeaveGroupAsync` (2026-10-06):** guruhdan
  **chiqarish** (`RemoveMember`) va **guruh almashtirishning ESKI tomoni** (`TransferMember`) shuni
  chaqiradi. AKTIV a'zolikda hisob muzlatishdagi kabi yopiladi (`SettleFreezeAsync` — chiqish oyi
  sanagacha QISMAN, keyingi oylar BEKOR), so'ng a'zolik `IsActive=false` + `LeftAt` bo'ladi.
  ⚠️ **Eski guruhda MUZLATILGAN bo'lib QOLMAYDI** — ilgari almashtirishda `frozen` + faol bo'lib qolar,
  o'quvchi ikki guruhda birdan ko'rinardi ("duplikat", foydalanuvchi shikoyati). `Status`
  o'zgartirilmaydi, chegara `LeftAt` (`NotLeftBeforeMonth`, ledger chiqish oyida to'xtaydi); guruh
  sahifasida u «Guruhdan chiqarilganlar» tarixida, profilda «Chiqgan» bo'lib turadi.
  ⚠️ **Chiqarish endi pulni ham yopadi** (ilgari faqat `IsActive=false` — oy o'rtasida chiqqanda to'liq
  oylik va oldindan yozilgan keyingi oylar qarz bo'lib qolardi). Oynada AKTIV a'zolik uchun
  **chiqish sanasi** so'raladi (`?date=`, bo'sh — bugun, kelajak — 400). Muzlatilgan/sinovdagi
  a'zolikka pul jihatidan tegilmaydi. Testlar: `MembershipLeaveTests`.
  Shu bilan birga (tekshiruvda topilgan, 2026-10-06):
  - chiqarishda o'quvchining «asosiy guruh» yorlig'i (`ClassName`) chiqqan guruhda qolmaydi (boshqa tirik
    guruhi yoki bo'sh) — aks holda portal chati, ro'yxat belgisi va guruhni o'chirish uni guruhda deb bilardi;
  - o'tkazishda sanalar tekshiriladi (aktivlashtirish chiqishdan OLDIN bo'lmaydi; chiqish KELAJAKDA
    bo'lmaydi — `RemoveMember` bilan bir xil); maqsad guruhda o'quvchi MUZLATILGAN bo'lsa — o'tkazish emas,
    «Aktivlashtirish» (sabab matni bilan 400); maqsad guruh ARXIVDA bo'lsa — 400. Orqaga sanalgan
    aktivlashtirishda oraliq oylar DARHOL yoziladi (`AccrueCatchUpAsync`, yakka aktivlashtirishdagi kabi).
    Oynada eski guruhdan chiqish SABABI tanlanadi (`remove_*` kategoriyasi, «Ketish sabablari» uchun);
  - qo'lda «chiqarish + qo'shish + aktivlashtirish» oqimida avans shu oyda CHIQQAN a'zolikdan ham ko'chadi.
- ⚠️ **HECH aktivlashtirilmagan (sinov) a'zolik muzlatilsa oylik YOZILMAYDI** (`SettleFreezeAsync`):
  ilgari bo'sh `ActivatedAt` "oy boshidan faol" deb olinib, ommaviy muzlatishda sinovdagilarga oyning
  1-kunidan beri darslar hisoblanardi.
- ⚠️ **Shu OYDA chiqib qayta qo'shilsa/qaytarilsa oldingi segment SAQLANADI**
  (`MembershipLifecycle.ClosedPeriodEndsInMonth` → `addSegment`): ilgari faqat muzlatishdan qaytishda
  qo'shilardi, qayta qo'shish va o'tkazib qaytarishda esa oy boshidagi darslar yangi segment bilan
  almashib, hisobdan yo'qolardi.

- **OMMAVIY MUZLATISH / AKTIVLASHTIRISH** (bir paytda ko'p o'quvchi). Ikki kirish nuqtasi:
  guruh sahifasi a'zolar modalidan (`POST /api/admin/classes/{id}/members/bulk-freeze` va
  `.../bulk-activate` — faqat SHU guruh a'zoliklari) va **o'quvchilar ro'yxatidan**
  (`POST /api/admin/classes/members/bulk-freeze` / `bulk-activate` — guruhsiz, ya'ni tanlangan
  o'quvchining **BARCHA faol a'zoliklari**).

  ⚠️ **Yangi hisob-kitob YO'Q.** Ikkala yo'l ham `ClassesController.FreezeCoreAsync` /
  `ActivateCoreAsync` ni chaqiradi — AYNAN o'sha metodlarni yakka `FreezeMember`/`ActivateMember`
  ham chaqiradi. Ya'ni qisman oy to'lovi, orqaga sanalgan hisoblarni bekor qilish, avans ko'chishi,
  catch-up oylar va **auditga har o'quvchi uchun ALOHIDA qator** — hammasi o'zgarishsiz.

  ⚠️ **Har a'zolik ALOHIDA `SaveChanges` bilan saqlanadi**: bittasi xato bersa qolganlari baribir
  bajariladi (`contacts` bulk qoidasi bilan bir xil mantiq — 100 tadan bittasi tufayli hech kim
  tushib qolmasin). Xato bergan a'zolikning yarim o'zgarishlari `ChangeTracker.Clear()` bilan
  bekor qilinadi, aks holda ular KEYINGI o'quvchining saqlashi bilan bazaga tushib ketardi.

  ⚠️ **`studentIds` BO'SH bo'lishi mumkin emas** — "hammasi"ni server o'zi qidirmaydi. Tanlov har
  doim UI'da ko'rinib turadi, ya'ni tasodifan butun guruhni muzlatib qo'yish yo'li ochilmaydi.

  Qaysi a'zolik tushishi — `MembershipBulk.IsEligible` (sof funksiya, `MembershipBulkTests`):
  muzlatishga `active`/`trial`, aktivlashtirishga `active` dan boshqasi. Filtr **SQL'da EMAS**,
  shu funksiyada — qoida bir joyda tursin va mos kelmagan a'zolik jimgina yo'qolmasin: u **xato
  emas**, javobda `skipped` bo'lib sanaladi va `noMembership` ("umuman a'zoligi yo'q") bilan
  ARALASHMAYDI. Chegara `MembershipBulk.MaxTargets` = 500 a'zolik.

  Javob (`BulkMembershipResultDto`) "nima bo'ldi"ni to'liq ochadi:
  `changed` · `students` · `skipped` · `failed` · `noMembership` · `restored` · `movedAdvance` ·
  `catchUpMonths` · `errors[]` — jimgina tushib qolgan o'quvchi bo'lmasin.

- **GURUHNI TUGATISH (SERTIFIKAT BILAN)** (`POST /api/admin/classes/{id}/complete-and-transfer`,
  guruh sahifasi "⋮" → "Tugatish (sertifikat bilan)"): modalda **IKKITA SANA** so'raladi —
  `closeDate` (eski guruh yopiladigan sana) va `activateDate` (yangi guruhda aktivlashtirish sanasi;
  `activateInNewGroup` ptichkasi bilan o'chirilsa o'quvchilar yangi guruhda "sinov"da qoladi).
  Oqim `TransferMember` (guruh almashtirish) bilan bir xil, faqat OMMAVIY: eski guruhdagi FAOL
  a'zoliklar `closeDate`dan muzlatiladi (`MembershipBilling.SettleFreezeAsync` — shu sanagacha o'qilgan
  darslar uchun oylik **ESKI GURUHGA** yoziladi, keyingi oylar bekor qilinadi), a'zolik
  `Status="completed"`, `IsActive=false`, `LeftAt=FrozenAt=closeDate` bo'ladi (FrozenAt — hisob-kitob
  chegarasi: `StudentGroupLedger`/`GroupBalanceService`/`SalaryLedger`/`RetentionBonusService` hammasi
  shunga qaraydi), sertifikat beriladi, guruh `IsArchived`+`Status="archived"` bo'ladi, yangi guruhda
  esa eski guruhda FAOL bo'lganlar `activateDate`dan aktivlashtiriladi
  (`ChargeActivationProrateAsync` + `AccrueCatchUpAsync` + `CarryGroupAdvanceAsync` — eski guruhda
  ortib qolgan avans yangi guruhga ko'chadi). Sinov/muzlatilgan a'zolar yangi guruhga ko'chirilmaydi.
  ⚠️ Ilgari bu endpoint hisobga UMUMAN tegmasdi: a'zolik shunchaki "completed" bo'lib yopilar,
  yopish oyining allaqachon yozilgan TO'LIQ oyligi kamaymas, hisob umuman bo'lmagan holatda esa oy
  "to'langan" bo'lib ko'rinardi va eski guruhga qarz yozilmasdi.
  Javob: `CloseDate/ActivateDate/ChargedOldGroup/RestoredCharges/ActivatedInNew/MovedAdvance`.
  `StudentGroupLedger` ham chiqish oyini endi QISMAN ko'rsatadi (`!IsActive && LeftAt` shu oyda →
  `FreezeGross`), to'liq oylik emas — hisob qatori hali yozilmagan holatda ham.
  ⚠️ **2026-10-07 dan (audit MEM-2):**
  - **FAQAT AKTIV a'zo kursni tugatadi** (`GroupCompletionRules.Completes`): sertifikat + `completed`
    faqat unga. SINOVDAGI va MUZLATILGAN (va arxivdagi o'quvchining) a'zoligi ODDIY CHIQISH bilan
    yopiladi (`LeaveGroupAsync`: `LeftAt`, holat o'zgarmaydi, sertifikat YO'Q) va sabab jurnaliga tizim
    sababi tushadi (`retention-loss.md` §3). Ilgari ular ham "bitiruvchi" bo'lib sertifikat olardi.
  - **BITTA TRANZAKSIYA** — yopish, yangi guruh, ko'chirish, sertifikatlar va audit: yo hammasi, yo
    hech narsa. Har a'zoga alohida `Membership` audit qatori + yig'ma qator SAQLASHDAN OLDIN (ilgari
    yig'ma qator oxirgi `SaveChanges`dan keyin yozilib, bazaga tushmasdi).
  - Yangi guruhga **o'qituvchi tarixi** ochiladi (`GroupTeacherHistory.AssignAsync`, boshlanishi =
    aktivlashtirish sanasi); yangi guruh nomi NOYOB bo'lishi shart (eski nom arxivdagi guruhda qoladi).
  - Ko'chirilmaganlarning `Student.ClassName` yorlig'i bo'shatiladi (`ReleasePrimaryGroupNameAsync`);
    ko'chirilganniki faqat u eski guruh nomi (yoki bo'sh) bo'lsa yangi nomga o'tadi. Bir xil fandagi
    chegirma yangi guruhga ko'chadi (`discounts.md` §5.5). Yopish kunidan keyingi o'rinbosar sanalari
    olib tashlanadi (`substitute-teachers.md` §9). Ruxsat — **FAQAT superadmin** (HARD rol, UI bilan bir xil).

- **GURUHNI YOPISH** (`POST /api/admin/classes/{id}/close`, guruh sahifasi "⋮" → "Guruhni yopish"):
  berilgan sanadan guruhning BARCHA faol a'zoliklari muzlatiladi (har biriga oddiy muzlatish bilan bir
  xil qisman to'lov), muzlatish oyidan KEYINGI hisoblar bekor qilinadi
  (`TuitionService.PurgeChargesAfterMonthAsync` — orqaga sanalgan yopishda qarz sanadan keyin o'smasin;
  `Locked` tegilmaydi), trial a'zoliklar yakunlanadi (hisobi yo'q — muzlatilsa soxta qarz chiqardi),
  guruh `IsArchived`+`Status="archived"` bo'ladi. O'quvchilar ARXIVLANMAYDI va a'zoliklar saqlanadi —
  **muzlatilgan/arxiv guruhga to'lov qabul qilinaveradi**: `PaymentModal` trial'dan boshqa barcha
  a'zoliklarni ko'rsatadi, `StudentGroupLedger` esa muzlatilgan/chiqarilgan a'zolikda oylarni
  muzlatish (yoki chiqish) oyida to'xtatadi va avans oylarini ko'rsatmaydi.

- **GURUHNI ARXIVLASH** (`POST /api/admin/classes/{id}/archive`, guruhlar ro'yxatidagi "Arxivlash"):
  **YOPISH bilan AYNAN bir xil yadro** (`ClassesController.CloseMembersAsync`), sana — BUGUN, sabab
  so'ralmaydi. Ya'ni a'zoliklar muzlatiladi + qisman to'lov + keyingi oylar hisobi bekor qilinadi.

  ⚠️ **RUXSAT (2026-10-07):** yopish, arxivlash va arxivdan chiqarish serverda ham
  `classes.list:delete` talab qiladi (`MayCloseGroups`, UI'dagi `can('classes.list','delete')` bilan bir
  xil) — ilgari POST bo'lgani uchun faqat "qo'shish" ruxsati yetardi. Yopish/arxivlashda a'zolarning
  `Student.ClassName` yorlig'i bo'shatiladi va yopish kunidan keyingi o'rinbosar sanalari olib tashlanadi.

  ⚠️ **ARXIVDAGI O'QITUVCHI faol guruhga biriktirilmaydi** (2026-10-08, audit R1-10): guruh yaratish,
  tahrirlash (faol guruh) va arxivdan chiqarish — 400 sabab bilan. Aks holda darslar maoshda hech kimga
  yozilmasdi (arxiv davri `InactivePeriods`) va jurnalni hech kim yuritmasdi. Arxivdagi (yopilgan)
  guruhni tahrirlash bloklanmaydi.

  ⚠️ **ARXIVDAN CHIQARISHDA TUGASH SANASI TOZALANADI** (`GroupLifecycleRules.EndDateOnUnarchive`):
  yopish qo'ygan (arxiv kunidan kech bo'lmagan) `EndDate` olib tashlanadi — aks holda qayta ochilgan
  guruhning darslari jadvaldan, o'qituvchi ilovasidan va «Davomat olinmagan» monitoridan yo'qolar, oylik
  esa yozilaverardi. Kelajakdagi (rejalashtirilgan) sana qoladi; guruh formasida «Tugash sanasi» maydoni
  bor (bo'sh — davom etadi).

  ⚠️ **ARXIVDAGI GURUHDA AKTIVLASHTIRISH YO'Q** (`MembershipBulk.ActivationBlockReason`): yakka
  aktivlashtirish, guruhga qo'shish va boshqa guruhga o'tkazishning maqsadi arxivdagi guruh bo'lsa — 400
  (sabab bilan); OMMAVIY aktivlashtirishda bunday a'zolik o'tkazib yuboriladi va javobda alohida
  (`skippedArchived` + `skipNotes`, "guruh arxivda"). Sabab: sentabrdagi ommaviy aktivlashtirish eski
  yopilgan guruhdagi muzlatilgan a'zolikni ham uyg'otib, ikki guruhdan oylik yozardi. Arxivdagi O'QUVCHI
  ham shunday (u ro'yxatlarda ko'rinmaydi). Guruh sahifasida «Talabalarni boshqarish» arxiv guruhda yo'q.

  ⚠️ **O'QUVCHILAR ARXIVLANMAYDI (2026-09-10 dan).** Ilgari arxivlash shu guruhdagi (va boshqa
  arxivlanmagan guruhda o'qimaydigan) o'quvchilarni `Student.IsArchived = true` qilardi — ular
  o'quvchilar ro'yxatidan butunlay yo'qolib, faqat «Arxiv» tabida qolardi. Foydalanuvchi talabi:
  o'quvchi arxivga TUSHMASIN, shunchaki **«Aktiv emas»** ro'yxatida qolsin.

  ⚠️ **Faqat arxivlashni olib tashlash YETMASDI** — a'zolikni muzlatish SHART: `AccrueMonth`
  guruhning `IsArchived` bayrog'iga **qaramaydi** (`db.Classes.ToListAsync()`, filtr yo'q), ya'ni
  a'zolik `active` bo'lib qolsa o'quvchiga har oy yolg'on qarz yozilib, ota-onalarga to'lov
  eslatmasi SMS'i ketaverardi (`.claude/rules/membership-periods.md` §6).

  ⚠️ `StudentMembershipView.ArchivableWithGroupAsync` **O'CHIRILDI** — endi hech kim o'quvchini
  guruh bilan birga arxivlamaydi. `Unarchive` dagi `ArchivedWithClass` tiklash tarmog'i esa
  QOLDIRILDI: u eski ma'lumotdagi (bu o'zgarishdan OLDIN arxivlangan) o'quvchilarni qaytaradi.
  ⚠️ **Ular ORQAGA HISOBLANMAYDI (2026-10-07, `GroupUnarchive`):** eski arxivlash a'zolikni `active`
  qoldirgan — o'quvchi qaytgach 12 soatlik `AccrueDue` u arxivda turgan HAR OYGA qarz (va "oylik
  hisoblandi" SMS) yozardi. Endi qaytarilgan o'quvchining BARCHA tirik `active` a'zoliklari o'quvchi
  arxivlangan sanadan **MUZLATILGAN** holatda tiklanadi (pulga tegilmaydi, har biriga audit qatori);
  davom ettirish — ongli «Aktivlashtirish». Qayta faollashtirishni avtomatik qilish ATAYIN tanlanmadi:
  oyning qolganini hisobsiz qoldirardi yoki qisman oylikni ikki marta yozardi, guruhning qolgan
  a'zoliklari ham arxivdan chiqqanda baribir "muzlatilganicha qoladi".

  ⚠️ Arxivdan chiqarilganda a'zoliklar **muzlatilganicha qoladi** (yopishdagi bilan bir xil) —
  avtomatik aktivlashtirish qisman oylikni ikki marta yozib yuborardi; kerak bo'lsa qo'lda
  aktivlashtiriladi.

- **O'QUVCHINI ARXIVLASH = GURUHDAN CHIQISH** (`POST /api/admin/students/{id}/archive`, 2026-10-07):
  har tirik a'zolik «Guruhdan chiqarish» bilan AYNAN bir xil yopiladi (`MembershipBilling.LeaveGroupAsync`
  — chiqish oyi qisman, keyingi oylar bekor). Arxivdan qaytarish a'zolikni TIKLAMAYDI (guruhsiz qaytadi);
  ESKI arxivdagilar tiklanganda a'zolik ARXIV SANASIDA yopiladi — arxiv oylari orqaga hisoblanmaydi.
  Batafsil: `.claude/rules/membership-periods.md` §10.
- **GURUHNI O'CHIRISH** (`DELETE /api/admin/classes/{id}`, faqat xato ochilgan guruh uchun; 2026-10-07):
  ⚠️ **Pul tarixi bor guruh O'CHIRILMAYDI** — guruhga teglangan (bekor qilinmagan) o'qish to'lovi yoki
  vozvrat, yoki eski teglanmagan to'lovning shu guruhga tushadigan ulushi (per-guruh balans qoidasi)
  bo'lsa 400 "Bu guruhda to'lovlar bor — o'chirib bo'lmaydi. Guruhni arxivlang."
  (`GroupDeletion.MoneyBlockReasonAsync`). Ilgari hisoblar TO'LANGANIGA qaramay balansga qaytarilardi:
  9 oy to'lagan o'quvchi 2,7 mln "avans" olar, to'lovlar esa guruh tegisiz qolib boshqa guruhlar
  o'qituvchilariga taqsimlanardi. Faqat TO'LANMAGAN hisoblar qolgan guruhda ular balansdan olib tashlanadi.
  O'quv/yordamchi ma'lumot ham o'sha tranzaksiyada o'chadi (`GroupDeletion.PurgeLearningDataAsync`):
  mezonli baholar, ball tuzatishlari, guruh mezonlari, dars ko'chirishlari, dastur o'tilishi, guruh
  testlari (ballar, tashqi natijalar, sertifikatlar va FAYLLARI), o'rinbosarlar, o'qituvchi tarixi, AI
  tahlillari, «Chegirma (kelmagan darslar)», ketish sabablari jurnali, guruh CHATI (nom boshqa guruhda
  bo'lmasa); amaldagi guruh chegirmalari bekor qilinadi. QOLADI: o'qituvchi haqidagi fikrlar, lid sinov
  darslari, uzaytirish hodisalari (snapshot), o'quvchining mashq urinishlari, xabar tarixi, audit.

- **PER-GURUH BALANS (qizil/yashil):** guruh kontekstidagi ro'yxatlar (jurnal qatorlari, guruh a'zolari —
  admin ham, o'qituvchi ilovasi ham) `Student.Balance` (UMUMIY) emas, `GroupBalanceService.ForGroupAsync`
  hisoblagan **shu guruh** balansini ko'rsatadi: `to'langan(shu guruh) − hisoblangan(shu guruh)`,
  manfiy = qarz. Shu sabab 2 kursda o'qiydigan o'quvchi bittasiga to'lasa — o'sha o'qituvchida YASHIL,
  to'lamaganida QIZIL. Teglanmagan (eski, GroupId=null) hisob VA to'lov guruhlar `MonthlyFee` nisbatida
  taqsimlanadi — `SalaryLedger`/`CourseFinanceReport` bilan bir xil konvensiya (per-guruh balanslar
  yig'indisi umumiy balansga teng chiqadi). DIQQAT: markaz bo'yicha qarzdorlik (Dashboard, qarzdorlarga
  SMS, o'quvchi profili/portali) ATAYIN umumiy `Student.Balance`da qoladi.
  **UCHINCHI RANG — 2+ OYLIK QARZ (fuchsia):** `GroupBalanceService.DetailedForGroupAsync` balans bilan
  birga **qarzdor OYLAR sonini** (`GroupBalanceInfo.DebtMonths`) qaytaradi — o'sha bir so'rovdagi
  hisob/to'lov OYMA-OY yig'iladi (teglanmagan yozuvlar o'sha `MonthlyFee` nisbatida taqsimlanadi) va
  `StudentGroupLedger`dagi qoida takrorlanadi: `hisoblangan(oy) − o'sha oyga to'langan > 0` → oy qarz
  (har oy MUSTAQIL — keyingi oyga to'langan avans o'tgan oy qarzini yopmaydi). `ForGroupAsync` — shu
  metodning o'rami, balans raqami o'zgarmagan. Jurnalda (admin ham, o'qituvchi ham — bitta
  `GroupJournalStudentDto.DebtMonths`) 2+ oy qarz **qizildan USTUN** va binafsha-pushti ko'rsatiladi;
  frontend ranglari yagona joyda — `lib/utils.ts` `balanceTextCls/balanceDotCls/balanceTitle`,
  chegara `HEAVY_DEBT_MONTHS`.

- **GURUH ALMASHTIRISHDA AVANS YANGI GURUHGA KO'CHADI** (`TuitionService.CarryGroupAdvanceAsync`):
  o'quvchi oy boshida ESKI guruhga to'lab, so'ng boshqa guruhga o'tkazilsa — muzlatish qisman hisobi eski
  guruh hisobini (oy boshidan muzlatilsa) nolga tushiradi, lekin PUL eski guruhga teglangan qolardi:
  eski guruh yashil (avans), yangi guruh esa to'liq qizil ("to'lamagan") bo'lib ko'rinardi. Endi eski
  guruhda **ortib qolgan summa** (to'langan − vozvrat − hisoblangan, HAR OY uchun alohida; faqat muzlatish
  oyi va undan KEYINGI oylar) yangi guruhga qayta teglanadi: to'lov to'liq ortiqcha bo'lsa `GroupId`
  almashadi, qisman bo'lsa tranzaksiya ikkiga bo'linadi (asl yozuv kamayadi + yangi guruhga yangi yozuv,
  izohda "[guruh almashtirildi: A → B]"). Umumiy pul, `Student.Balance` va kassa hisobotlari
  O'ZGARMAYDI — faqat guruh tegi (shu sabab foizli maosh ham to'g'ri o'qituvchiga o'tadi). Chaqiriladi:
  `TransferMember` (guruh almashtirish tugmasi) va `ActivateMember` — ikkinchisida faqat **AYNAN SHU OYDA**
  muzlatilgan boshqa a'zolikdan (qo'lda "muzlatish + yangi guruhga qo'shish" oqimi); ilgari muzlatilgan
  (ta'tildagi) a'zolik avansi tegilmaydi. Ko'chirilgan summa auditga yoziladi. Eski (fiks'dan oldingi)
  yozuvlarni tuzatish: Moliya → to'lovni tahrirlash → guruhni yangi guruhga o'zgartirish.

- **GURUH YOPILGACH MAOSH YO'QOLMAYDI** (`SalaryLedger`, migratsiya KERAK EMAS): guruh yopilib
  o'quvchilar muzlatilgach o'qituvchining o'tgan oy maoshi uch xil sabab bilan "yozilmay" qolardi.
  Uchalasi ham tuzatildi (`TeacherSalaryClosedGroupTests`):
  1. **Davr.** Standart `from` — `AcademicYearStartMonthAsync` (ARXIVLANMAGAN o'quvchilarning eng
     erta kelgan oyi); guruh bilan birga o'quvchilar arxivlangach u JORIY oyga sakrar va **o'tgan
     oy qatori jadvaldan umuman tushib qolardi**. Endi standart davr **oxirgi 6 oyni**
     (`RecentMonths`) va maosh BERILGAN eng erta oyni HAR DOIM qamrab oladi (`MaxLookbackMonths = 24`
     chegarasi bilan). Markaz odatdagidek ishlayotganda akademik yil boshi baribir ertaroq bo'ladi
     va bu blok hech narsani o'zgartirmaydi. `from` aniq berilsa (Moliya → O'qituvchilar davri) —
     umuman tegilmaydi.
  2. **Jurnal ushlanmasi.** Rejadagi darslar guruh `Days` idan chiqariladi, chegara esa faqat
     `Group.EndDate` edi — u "Yopish" yo'lida qo'yiladi, **"Arxivlash" yo'lida esa YO'Q**. Natijada
     arxivdan keyingi barcha "darslar" belgilanmagan hisoblanib, ushlanma butun oylikni yeb qo'yardi.
     Endi chegara = `EndDate` va `ArchivedAt` dan ERTAROG'I (`SalaryLedger.LessonEnd`).
  3. **Foizli maoshda 0.** Foiz YIG'ILGAN puldan hisoblanadi; guruh yopilib pul hali kelmagan oyda
     maosh 0 bo'lib ko'rinardi. Endi `MonthSalaryDto` da ikkita yangi maydon bor: **`Charged`**
     (o'quvchilarga shu oy uchun hisoblangan, chegirma ayrilgan — qarz ham kiradi) va
     **`PotentialExpected`** ("hammasi to'lansa" maosh, ushlanma ayrilgan). Manba —
     `SalaryLedger.PercentBasesAsync` (ilgari `CollectedPerGroupAsync`): u endi IKKITA xarita
     qaytaradi va **hisoblangan** ham to'lovlar bilan AYNAN bir xil qoida bo'yicha taqsimlanadi
     (teglangan qator 100% guruhga, teglanmagan — `MonthlyFee` nisbatida).
     ⚠️ `Expected` MA'NOSI O'ZGARMADI — pul kelmaguncha maosh baribir hisoblanmaydi; yangi raqam
     faqat YONIDA ko'rsatiladi ("hisob bo'yicha").
  UI: o'qituvchi profili → Maosh tabida **«O'tgan oy hisoblandi»** kartochkasi; oylar jadvalida,
  Moliya → `TeacherSalaryDetailModal` da va o'qituvchi ilovasining Maosh ekranida "hisob bo'yicha"
  qatori (faqat `PotentialExpected > Expected` bo'lganda). **Per-guruh maosh sozlagichi endi
  ARXIVLANGAN guruhlarni ham ko'rsatadi** ("Yopilgan" belgisi bilan) — ilgari yagona guruhi
  yopilgan o'qituvchida bo'lim butunlay yo'qolib "Guruh biriktirilmagan" chiqardi.

- **O'TGAN OY KIMNIKI — TARIX, HOZIRGI `Group.TeacherId` EMAS (audit C3, 2026-10-07, migratsiya KERAK EMAS):**
  maosh varaqasi (`SalaryLedger`) o'qituvchi HOZIR va TARIXDA (`GroupTeacherAssignment`) o'qitgan guruhlarni
  oladi; har (guruh, oy) ulushi = o'sha oyning REJADAGI dars sanalaridan (`SubstituteTeacherService
  .LessonDatesInMonth` — ko'chirishlar + guruh umri) qanchasi shu o'qituvchiga tegishli
  (`GroupTeacherHistory.TeacherOnDate`/`SharesInMonth`, yuklovchi — `GroupOwnership`). Almashuv kuni —
  YANGI o'qituvchiniki; tarixning birinchi qatoridan oldingi sana — birinchi o'qituvchiniki; tarix
  umuman yo'q — hammasi `Group.TeacherId` ga (ilgarigi xulq).
  ⚠️ Ilgari guruh `Group.TeacherId` bo'yicha tanlanardi: o'qituvchi almashsa guruhning BUTUN o'tmishi
  (to'langan oylar ham) yangisiga ko'chib, eskisi "ortiqcha olgan", yangisi "olmagan" bo'lib ko'rinardi
  (ikki marta to'lashga undardi). Endi ikki varaqaning yig'indisi bitta o'qituvchidagiga TENG
  (`AuditFixT4Tests`). Foiz bazasi, qat'iy summa, jurnal jarimasi (faqat O'Z sanalari — boshqa
  o'qituvchining darsi rejadan chiqariladi) va o'rinbosar ushlanmasi (qamralgan SANA egasidan, saqlangan
  `OriginalTeacherId` dan emas) — hammasi shu ulush bilan. Xuddi shu qoida: `CourseFinanceReport`
  (o'qituvchi kesimi), `TeacherActivityReport`, `TeacherSnapshotBuilder`, o'rinbosar narxidagi "asosiy
  o'qituvchi" (`PerLessonBatchAsync`, oy egasi).
  ⚠️ `GroupTeacherHistory.AssignAsync`: tarixsiz guruhda o'qituvchi almashsa, avval ESKI o'qituvchining
  qatori guruh boshidan (StartDate → eng erta JoinedAt) bugungacha yoziladi — aks holda o'tmish
  yangi o'qituvchiga "birinchi qator egasi" sifatida yozilib qolardi.
  ⚠️ Per-guruh maosh sozlamasi hamon VERSIYALANMAYDI: guruh yangi o'qituvchida boshqa foizga o'tkazilsa,
  eski o'qituvchining o'tgan oylari ham yangi foizda ko'rinadi (avvaldan shunday).

- **QAT'IY SUMMA FAQAT AMAL QILGAN DAVRDA (audit C4):** `SalaryLedger.MonthFraction(oy, dan, gacha)` —
  oyning shu oraliqqa tushgan kunlar ulushi, IKKALA chekka oy qisman. Guruh qat'iy summasi —
  `[StartDate (yo'q bo'lsa tarixdagi birinchi biriktirish), LessonEnd(EndDate, ArchivedAt)]` ∩
  o'qituvchining `[ishga kirgan kun, arxivlangan kun]`; o'qituvchi qat'iy oyligi (`Teacher.Salary`) —
  ishga kirgan kundan `Teacher.ArchivedAt` gacha. ⚠️ Ilgari koeffitsient faqat BOSHLANISH oyini
  qisqartirardi: yopilgan guruhning summasi va arxivdagi o'qituvchining oyligi har oy yozilib, qoldiq
  o'sib borar edi; oktyabrda ochilgan qat'iy guruh o'tgan oylarga ham qo'shilardi.
  ⚠️ **ARXIVDA TURGAN DAVR DOIM HISOBLANMAYDI** (migratsiya: `Teachers.InactivePeriods`, `text[]`):
  arxivdan qaytarishda (`Restore`) `ArchivedAt` tozalanadi, shuning uchun davr `Teacher.InactivePeriods`
  ga yoziladi — `"arxiv kunidan keyingi kun|qaytarilgan kundan oldingi kun"` (ikkala chegara KIRADI;
  arxiv kuni va qaytarilgan kun — ishlangan kunlar; bir kunda qaytarilsa yozuv yo'q). Hozir arxivda
  bo'lsa — `ArchivedAt` dan keyingi hamma kun. `SalaryLedger.InactiveSpans` shu kunlarni: qat'iy
  oylikdan va guruh qat'iy summasidan (`MonthFraction`), FOIZLI guruh ulushidan (o'sha kunlardagi
  darslar unga yozilmaydi — hech kimga o'tmaydi: boshqa o'qituvchining varaqasi tarix bo'yicha
  o'zgarmaydi), jurnal rejasidan va o'rinbosar ushlanmasidan chiqaradi. Ilgari qaytarilgan
  o'qituvchiga arxivdagi oylar uchun qat'iy oylik qaytadan "hisoblanib" qolardi.

- **O'QITUVCHINI ARXIVLASH = ISHDAN KETDI (audit SAL-2, `TeachersController.Archive`):** FAOL guruhi bor
  o'qituvchi arxivlanmaydi (`Delete` bilan BITTA darvoza — 400 va guruhlar nomlari); kelajakdagi
  o'rinbosarlik kunlari (o'rinbosar ham, asosiy ham) bekor qilinadi
  (`SubstituteTeacherService.CancelFutureForTeacherAsync`, har tayinlovga audit); qat'iy oylik arxiv
  kunida to'xtaydi (yuqorida); to'lanmagan maosh qoldig'i bo'lsa javobda `unpaidSalary` + `warning`
  (UI ogohlantiradi va «Maosh» bo'limiga yo'naltiradi). Qaytarishda (`Restore`) arxiv davri
  `InactivePeriods` ga yoziladi (yuqorida); parol yo'q bo'lsa `loginBlocked: true` — UI yangi parol
  yaratishni taklif qiladi (mavjud `reset-password` yo'li).
  Per-guruh foiz 0..100 dan tashqarida — 400 (`UpdateGroupSalaries`).

- **O'RINBOSARLIK HAQI `Expected` GA QANDAY KIRADI** (nol yig'indili model — to'liq qoida:
  `.claude/rules/substitute-teachers.md`): o'rinbosar begona guruhda o'tgan darslar uchun
  `MonthSalaryDto.SubstituteFee` **`Expected` ga QO'SHILADI**, asosiy o'qituvchidan esa AYNAN
  o'shancha (`SubstituteDeduction`) AYIRILADI. Ikkalasi ham bitta hisoblagichdan
  (`SubstituteTeacherService.PerLesson`) va bitta bazadan — guruhning shu oydagi maosh ulushi
  (foizli rejimda **YIG'ILGAN** pul × foiz), oyning HAQIQIY dars soniga bo'lingan.
  ⚠️ `Expected` MA'NOSI SHU BILAN HAM O'ZGARMAYDI: o'rinbosarlik haqi ham YIG'ILGAN puldan
  chiqadi, ya'ni "pul kelmaguncha maosh hisoblanmaydi" qoidasi buzilmaydi — pul kelmagan oyda
  o'rinbosarlik haqi ham 0 bo'ladi.
  ⚠️ **JURNAL JARIMASINING MAXRAJIGA KIRMAYDI:** legacy-qat'iy rejimda bitta dars narxi
  `ownWorkBase / plannedTotal` dan hisoblanadi, bunda `ownWorkBase` — o'rinbosarlik haqi
  QO'SHILMAGAN baza. Sabab: jarima o'qituvchining O'Z guruhlaridagi belgilanmagan darslarga
  tegishli; boshqa guruhda o'tilgan dars haqini maxrajga qo'shish jarimani sun'iy
  kattalashtirardi. Shuningdek o'rinbosar qamragan sanalar asosiy o'qituvchining
  `Planned`/`Missed` sanog'idan CHIQARILADI (`SalaryJournalStats.BuildAsync(excludeDates:)`) —
  aks holda bitta dars uchun IKKI marta jarima bo'lardi (jurnal jarimasi + o'rinbosarlik
  ushlanmasi).

- **OY TAFSILOTIDA TUSHUM HAM KO'RINADI** (o'qituvchi profili → Maosh → oy tanlanganda; migratsiya
  KERAK EMAS): bitta kartochkada beshta raqam — **tushum bo'lishi kerak** (`MonthSalaryDto.TuitionCharged`)
  · **tushum bo'ldi** (`TuitionCollected`) · **maosh hisoblandi** (`Expected`) · **berildi** (`Paid`) ·
  **qoldi** (`Remaining`), yonida yig'ilmagan qarz va yig'ilish foizi.
  ⚠️ `TuitionCharged/TuitionCollected` — o'qituvchining **BARCHA** guruhlari bo'yicha TUSHUM;
  `Charged/Collected` esa **MAOSH BAZASI** (faqat foizli ulushi bor guruhlar). Barcha guruhlari
  foizli o'qituvchida ular teng, ARALASH sozlamada FARQ qiladi — shuning uchun ikkita alohida
  maydon, biri ikkinchisining o'rniga ishlatilmaydi.
  Tuition bazasi odatda faqat FOIZLI maosh uchun o'qiladi; tushum raqamlari qat'iy maoshda ham
  kerak bo'lgani uchun `SalaryLedger.BuildAsync` ga **`withRevenue`** bayrog'i qo'shilgan.
  Standart `false` ATAYIN: **faqat** `GET /api/admin/teachers/{id}/salary-ledger` uni yoqadi.
  Moliya → "O'qituvchilar" hisoboti `BuildAsync` ni HAR BIR o'qituvchi uchun chaqiradi (yoqilsa
  qat'iy maoshlilarga ham beshta ortiqcha so'rov qo'shilardi), o'qituvchi ILOVASI esa qat'iy
  maoshli o'qituvchiga markaz tushumini ko'rsatmasligi kerak. Testlar: `TeacherSalaryRevenueTests`.
  Oy boshida hisob hali yozilmagan bo'lishi mumkin (`AccrueMonth` fon xizmati) — u holda "tushum
  bo'lishi kerak" 0 bo'lib, to'lov "avans" bo'lib ko'rinardi; UI bunda "hisob hali yozilmagan" deb
  yozadi.

- **Maoshni jurnalga bog'lash** (migratsiya `AddSalaryJournalPolicy`): "Jurnal boshqaruvi" modalidagi
  "Maosh va jurnal" bo'limi — `CenterMeta.SalaryRequireJournal` + `SalaryGraceDays` (0-30). Yoqilsa har
  oyda guruh `Days` bo'yicha REJADAGI darslardan jurnalda `LessonNote.Conducted` belgilanmaganlari
  **o'tilmagan** hisoblanadi va maoshdan ushlanadi (`SalaryJournalStats.BuildAsync` → (oy,guruh) →
  Planned/Conducted/MissedDates; muhlati kelmagan = `AppClock.Today − graceDays` dan keyingi darslar
  hisobga olinmaydi). `SalaryLedger`: per-guruh va legacy-foizda ushlanma har guruh ulushidan
  (`contribution × missed/planned`), legacy-qat'iyda bitta dars narxi = `oylik ÷ rejadagi darslar`.
  `MonthSalaryDto` — `BaseExpected/Deduction/Planned-Conducted-MissedLessons/Lessons[]`
  (`SalaryLessonStatDto` — guruh + belgilanmagan SANALAR); `SalaryLedgerDto` `TotalDeduction/JournalLinked`;
  `SalaryReportRowDto` `Deduction/MissedLessons`. **Sabab ko'rinadi:** Moliya → O'qituvchilar jadvalida
  "Ushlanma" ustuni, `TeacherSalaryDetailModal`da oy qatorini bosib guruh+sanalar, o'qituvchining
  `SalaryPage`ida ham xuddi shunday.

## SINFGA QARAB OYLIK (2026-10-05, migratsiya `AddSubjectPriceBands`)

Kursda **sinf oraliqlari** bo'lishi mumkin (`SubjectPriceBand`: 3–7-sinf 225 000, 8–9-sinf 275 000 …).
O'quvchining oyligi uning **o'sha oydagi** maktab sinfiga qarab tanlanadi. SPEC: `docs/SPEC-kurs-sinf-narxi.md`.

```
sinf (oyning 1-kuniga, SchoolGrades.Current) kurs oralig'iga tushsa → oraliq Price (+ oraliq LessonPrice)
aks holda (sinf yo'q / oraliqdan tashqarida / kurssiz guruh)          → guruh MonthlyFee (+ kurs LessonPrice)
```

- ⚠️ **YAGONA joy — `TuitionPricing` / `PricingBook`.** O'quvchi QARZIGA tegadigan yangi kod
  `Group.MonthlyFee` ni TO'G'RIDAN-TO'G'RI o'qimaydi: `PricingBook.Resolve(group, student, month)`.
  Hozir ulangan: `AccrueMonth`, `AccrueCatchUpAsync`, `EnsureChargeAsync`, aktivlashtirish va muzlatish
  qisman oyi, `ApplyGroupFeeToCurrentMonthAsync`, `StudentGroupLedger` (to'lov oynasi), `StudentLedger`,
  `SubscriptionRisk`/`IncomePlan`, shartnoma summasi, ota-ona ilovasi, guruh a'zolari narxi, chegirma oynasi.
- ⚠️ **ATAYIN guruh narxida qoladi** (guruh darajasidagi og'irliklar): teglanmagan to'lovni guruhlarga
  bo'lish (`SalaryLedger`, `GroupBalanceService`, `CourseFinanceReport`), KPI o'rtacha narx, kurs
  analitikasi tushumi, ushlab turish bonusi og'irligi, eski guruhsiz (ClassName) billing. Maosh foizi
  2026-09 dan HISOBLANGAN oylikdan (`MonthlyCharge.Amount`) — demak u avtomatik to'g'ri.
- 1-sentabrda sinf o'zi oshadi → sentabr hisobi yangi oraliq narxida yoziladi (alohida ish yo'q).
- Oraliq bir dars narxi `0` — pro-rata (oylik ÷ jami dars), `null` EMAS: oraliq topilganda kursning
  bir dars narxi ishlatilmaydi (u boshqa oylikka mo'ljallangan).
- ⚠️ **SINF NARXI DARHOL QO'LLANADI (2026-10-05, foydalanuvchi talabi: "shu zahoti avtomatik").**
  Uch yo'l, uchalasi ham SO'RAMASDAN:
  1. **Kurs sinf narxlari o'zgarsa** (`SubjectsController.Update`) — joriy oy hisoblari har o'quvchi uchun
     YANGI oraliqlar bilan qayta moslanadi (saqlanmagan oraliqlar `PricingBook.From(rows)` bilan beriladi —
     bazadan o'qilsa eskisi chiqardi). «Ha/Yo'q» endi FAQAT ASOSIY narx uchun so'raladi: «Yo'q» bo'lsa joriy
     oyda oraliqqa tushmaganlar eski asosiy narxda qoladi, tushganlar yangi sinf narxiga o'tadi.
  2. **Guruh boshqa kursga o'tkazilsa** (`ClassesController.Update`) — yangi kursning sinf narxlari ham shunday.
  3. **O'quvchining SINFI o'zgarsa** — `GradeRepricing.ApplyAsync` (YAGONA yo'l): admin tahriri, Excel
     importi (mavjud o'quvchiga sinf qo'shilishi) va o'qituvchi tahriri. JORIY va KEYINGI (oldindan
     to'langan) oylar qayta hisoblanadi, har oy tarixga alohida yoziladi («Oylik sinfga qarab qayta
     hisoblandi (7-sinf → 8-sinf) …», `StudentDiscount` turi). ⚠️ O'qituvchi sinfni tuzatsa ham pul o'zgaradi.
  Hammasida TEGILMAYDI: o'tgan oylar, `Locked` qatorlar (qo'lda tahrir, «Chegirma») va ESKI to'liq narxga
  teng bo'lmagan QISMAN oy (aktivlashtirish/muzlatish/chiqish). Sinf narxi YO'Q kursda sinf umuman
  ahamiyatsiz — hamma guruhning asosiy narxida qoladi. Testlar: `SubjectPriceBandBillingTests`
  (`Sinf_kiritilsa_…`, `Kurs_sinf_narxlari_ozgarsa_…`).
- `SubjectPayload.PriceBands`: `null` — tegilmaydi (eski mijoz), `[]` — hammasi o'chiriladi.
  Tekshiruv (`TuitionPricing.Validate`): 1 ≤ dan ≤ gacha ≤ 12, narx > 0, ustma-ust emas, ≤ 12 ta.
- Yozilgan hisoblar TEGILMAYDI (retroaktiv qayta hisob yo'q).
- ⚠️ **BEPUL guruhda (narx 0) oraliq QO'LLANMAYDI**, kurs formasi esa asosiy narxi 0 kursga oraliq
  qo'shishni rad etadi. Sabab: bunday guruhda hisob hech qachon yozilmagan — oraliq narxi qo'llansa
  `AccrueDue` (butun tarixni skanerlaydi) aktivlashtirishdan beri BARCHA oylarga orqaga qarz yozib,
  ota-onalarga SMS yuborardi.
- ⚠️ **«Joriy oydan qo'llash» endi KELGUSI (oldindan to'langan) oylarni ham qamraydi va TARIXGA yozadi
  (2026-10-07):** `ApplyGroupFeeToCurrentMonthAsync` joriy VA keyingi oylarning hisobini (avans bilan
  ochilganlarini) yangi narxga moslaydi — ilgari faqat joriy oy tegilardi va oldindan to'langan oy eski
  narxda qolib ketardi (sinf narxi esa kelgusi oylarni allaqachon hisoblardi). `audit` uzatilsa, balansi
  o'zgargan HAR o'quvchiga tarixda BITTA qator («Oylik guruh/kurs narxi o'zgargani uchun qayta
  hisoblandi: guruh, oy — eski → yangi», `StudentDiscount` turi, `GradeRepricing` naqshi) — o'quvchining
  to'lov tarixi panelida "qarzim nega o'zgardi" ko'rinadi. Qaytaradigan son — o'quvchilar soni.
  `ClassesController.Update` va `SubjectsController.Update` `audit` ni uzatadi.
- ⚠️ **«Joriy oydan qo'llash» (`ApplyGroupFeeToCurrentMonthAsync`) endi `oldFee` bilan:** narxi
  o'zgarmagan o'quvchi va ESKI to'liq narxga teng bo'lmagan (qisman — aktivlashtirish/muzlatish/chiqish
  oyi yoki qo'lda o'zgartirilgan) hisob TEGILMAYDI. Ilgari kurs/guruh narxi o'zgarganda qisman oy
  hisobi to'liq oylik bilan ustidan yozilardi (o'qilmagan darslarga qarz). Kurs shu so'rovda
  almashtirilsa YANGI/ESKI kurs id'si aniq uzatiladi (bazadan o'qilsa eskisi chiqardi).
- Ommaviy aktivlashtirish/muzlatish oraliqlarni BIR MARTA yuklaydi (`pricing` parametri, `lessonFee` naqshi).
- ⚠️ Bazada ko'p o'quvchida sinf kiritilmagan — ular guruh narxini to'layveradi.
- Testlar: `TuitionPricingTests`, `SubjectPriceBandBillingTests`; klient — `lib/__tests__/priceBands.test.ts`.

## TRANZAKSIYA TURLARI — markaz katalogi (migratsiya `AddTransactionTypes`)

Moliya → «Tranzaksiya turi»: markaz o'z nomlarini kiritadi («Arenda», «Kanstovar», «SAT oylik
to'lovlari»), to'rt bo'limda — **kirim · chiqim · vaucher · jarima**. Qo'shish/tahrirlash/
o'chirish — `tasks`dagi kabi oddiy CRUD (`TransactionTypesController`, ruxsat `finance.main`).

⚠️ **ENG MUHIM — TUR HISOB-KITOBNI O'ZGARTIRMAYDI.** Butun moliya mantig'i avvalgidek
`FinanceTransaction.Category` (tizim kodi: `tuition`, `salary`, `penalty`, `rent` ...) bo'yicha
ishlaydi: maosh foizi (`SalaryLedger`), oylik hisobi, kassir hisoboti, P&L, «Jarima» sahifasi.
Har tur `TransactionType.BaseCategory` orqali AYNAN shu kodlardan biriga bog'lanadi —
`.claude/rules/contacts.md` §3.66 dagi `ContactStage.BaseStatus` bilan bir xil sabab.
**Yangi ko'rsatkich yozsangiz `TypeId` ga QARAMANG.**

- `FinanceTransaction.TypeId` — FAQAT ko'rsatish uchun (jadvalda toifa yorlig'i o'rniga markazning
  o'z nomi). null = eski yozuv, u holda avvalgidek `financeCategoryLabel(Category)` chiqadi.
- Yo'nalish (`income`/`expense`) ALOHIDA so'ralmaydi — bo'limdan kelib chiqadi
  (`TransactionTypeCatalog.DirectionOf`: vaucher → kirim, jarima → chiqim).
- Seed FAQAT jadval BO'SH bo'lganda yoziladi (`Program.cs`) — foydalanuvchi o'chirgan tur har
  restartda qayta tug'ilmasin. Tizim ustunlaridagi "yo'q bo'lgani qo'shiladi" naqshi BU YERDA EMAS.
- ISHLATILGAN tur o'chirilmaydi (400 + nechta amalda ekani) — eski qatorlar nomsiz qolmasin.
- Testlar: `TransactionTypeCatalogTests` (har turning toifasi HAQIQATAN mavjudligini qulflaydi).

## To'lov BIR MARTA o'tadi (2026-09-25)

"Saqlash" bir necha marta tez bosilganda to'lov bir necha marta yozilardi. Himoya uch qatlamli:

1. **Klient — sinxron qulf** (`inFlightRef`) `PaymentModal` va `TransactionFormModal` da.
   ⚠️ `submitting` holatining o'zi yetmaydi: u keyingi renderda yangilanadi. `TransactionFormModal`
   da esa himoya umuman yo'q edi.
2. **Klient — so'rov kaliti** (`requestId`, `lib/requestId.ts`): bitta oyna ochilishida BIR XIL,
   muvaffaqiyatdan keyin yangilanadi. Xato bo'lsa kalit o'zgarmaydi, ya'ni qayta urinish
   allaqachon o'tgan (javobi kelmagan) to'lovni ikkinchi marta yozmaydi.
3. **Server — `PaymentIdempotency`**: o'quvchi bo'yicha qulf (tekshiruv + yozish ketma-ket) va
   kalit keshi (30 daqiqa). `PaymentIntake.AddAsync` va `FinanceController.Create` ishlatadi.
   ⚠️ Holat PROTSESS XOTIRASIDA: ilova bitta nusxada ishlaydi. Bir nechta nusxaga o'tilsa bu
   bazaga (unikal indeks) ko'chirilishi SHART.

Muvaffaqiyatli to'lovdan keyin global bildirishnoma chiqadi (`toast.success`, `lib/toast.ts`,
`components/ui/Toaster.tsx` ilovaning ildizida). Xatolar toast bilan EMAS, forma ichida
ko'rsatiladi (`error-visibility.md`).

### Bekor qilish yo'llari ham shu qulf ostida

Qulf kaliti HAMMA pul amalida bir xil — `"student:{id}"`: to'lov (`PaymentIntake`, `Create`),
vozvrat (`Refund`), o'chirish (`Delete`) va tahrir (`Update`, `UpdatePayment`). Shuning uchun
bir o'quvchining pul amallari navbat bilan bajariladi.

- **Vozvrat:** qulfsiz ikki bir zumdagi so'rov ikkalasi ham eski qoldiqni ko'rib, to'lovdan
  KO'P pul qaytarar edi. Endi qulf + oyna kaliti (`RefundPayload.RequestId`).
- **O'chirish:** ikkinchi bosish "allaqachon o'chirilgan" (404, matn bilan) oladi.
  ⚠️ **Vozvrati bor to'lov O'CHIRILMAYDI** (400): o'chirilsa qaytarilgan pul balansdan ikkinchi
  marta ayrilardi. Avval vozvrat(lar) o'chiriladi.
- **Balans qulf ICHIDA qayta o'qiladi** (`PaymentIntake` da `student` controllerda qulfdan
  oldin yuklangan): aks holda parallel amal ta'siri eski qiymat ustidan yozilib yo'qolardi.
- Klient: `RefundModal`, `PaymentEditModal`, `ReasonPromptModal` da sinxron qulf.
  `ReasonPromptModal.onConfirm` Promise qaytarsa, xatodan keyin tugma qayta yoqiladi.

Testlar: `PaymentIdempotencyTests`.

## Tranzaksiyani BEKOR QILISH — o'chirilmaydi (2026-09-26)

Migratsiya: `AddFinanceTransactionVoid` (`IsVoided`, `VoidedAt`, `VoidedBy`, `VoidedById`, `VoidReason`).

- `DELETE api/admin/finance/transactions/{id}` endi qatorni **o'chirmaydi**: `IsVoided = true`,
  kim/qachon/sabab yoziladi, balansga ta'siri qaytariladi (`StudentBalanceEffect`), audit "Bekor qilindi".
  Ikkinchi marta bekor qilish — 404 "allaqachon bekor qilingan". Vozvrati bor to'lov — 400.
  ⚠️ (2026-10-07) **Bo'lingan to'lov oilasi BIRGA bekor qilinadi** (har a'zoga alohida audit qatori),
  oilaning biror qismidan vozvrat bo'lsa — 400. **Kelgusi oy AVANSI bekor qilinsa** o'sha oyning hisobi
  ham qaytariladi (pastda «AVANS HISOBI»).
- ⚠️ **GLOBAL FILTR** (`AppDbContext`: `HasQueryFilter(t => !t.IsVoided)`) bekor qilinganlarni
  BARCHA so'rovlardan chiqaradi: balans, maosh, hisobotlar, kassa, kvitansiya nazorati (raqam
  qayta ishlatilishi mumkin), to'lov idempotentligi. Yangi so'rov yozganda hech narsa qilish shart emas.
- Ko'rsatish kerak bo'lgan joylar ataylab `IgnoreQueryFilters()`:
  - `GET transactions?includeVoided=true` — faqat Moliya jadvallari (Amallar, To'lovlar). Standart
    `false`: bu endpointdan jamini klientda hisoblaydigan sahifalar bor.
  - `GET students/{id}/ledger` — admin to'lov tarixida (`StudentLedger.VoidedPaymentsAsync`).
    ⚠️ O'quvchi/ota-ona ilovasiga BERILMAYDI.
- Klientda bekor qilingan qator ustiga chizilgan + «Bekor qilindi» belgisi (kim/qachon/sabab
  sichqoncha ustida) va amal tugmalarisiz; jamilar va CSV eksport `live` filtri bilan ularni sanamaydi.
- ⚠️ `IgnoreQueryFilters()` ni hisob-kitob uchun ISHLATMANG — faqat ko'rsatish uchun.
- Eski (2026-09-26 gacha) o'chirilganlar avvalgidek Arxivda (`ArchiveService`, "finance"). TIKLASHDA
  balans ta'siri QAYTA qo'llanadi (`StudentBalanceEffect` bilan bir xil): o'quv to'lovi `+`, VOZVRAT `−`
  (ilgari faqat to'lov qaytarilardi — tiklangan vozvrat balansni o'zgartirmasdi). Vozvrat asl to'lovi
  yo'q/bekor qilingan bo'lsa yoki jami vozvrat to'lovdan oshsa — 400 (Refund endpointidagi cheklov).
  Tiklash o'quvchi qulfi ostida va auditga yoziladi.
- Testlar: `FinanceVoidTests`.

## KASSALAR (2026-10-05, migratsiya `AddCashboxes`)

Moliya → «Kassalar» (`/admin/finance?tab=cashiers`). SPEC: `docs/SPEC-kassalar.md`. Har kirim/chiqim
bitta KASSAGA yoziladi (`FinanceTransaction.CashboxId`), kassa QOLDIG'I saqlanmaydi — tranzaksiyalardan
hisoblanadi (kirim − chiqim; bekor qilinganlar global filtr bilan tushadi). Qoida — `CashboxService`.

- **Qaysi kassaga** (`CashboxService.ResolveAsync`): oynada tanlangani → kiritgan xodimning O'Z kassasi
  (`Cashbox.ResponsibleUserId`) → «Asosiy kassa» → birinchi faol. ⚠️ Tanlangan kassa yo'q/arxivlangan
  bo'lsa — **400**, jimgina boshqa kassaga TUSHIRILMAYDI (pul kassir bilmagan kassaga yozilib, qoldiqlar
  yolg'on chiqardi).
- **Ulangan yo'llar:** `PaymentIntake` (admin to'lovi + Kassa portali), `FinanceController.Create`
  (kirim/chiqim/maosh), **vozvrat — ASL to'lovning kassasidan** (pulni qabul qilgan kassa qaytaradi;
  u arxivlangan bo'lsa qaytaruvchining standart kassasi), avans ko'chirishdagi bo'linish (asl kassa
  saqlanadi), arxivdan tiklash (kassasiz/yo'q kassa → standart). Tahrir (`Update`, `UpdatePayment`):
  `cashboxId` faqat ANIQ berilsa o'zgaradi, auditga "kassa A → B" yoziladi.
  ⚠️ **Yangi pul yozish yo'li qo'shsangiz — `CashboxService.ResolveAsync` ni chaqiring**, aks holda
  yozuv kassasiz qoladi va hech qaysi kassa qoldig'ida ko'rinmaydi.
- **«Asosiy kassa»** — `Id = "main"`: migratsiya yaratadi va BARCHA eski yozuvlarni unga ko'chiradi
  (foydalanuvchi qarori); yangi bazada `Program.cs` yaratadi. Arxivlanmaydi (zaxira kassa).
- **Ruxsat:** yaratish/tahrirlash/arxivlash/tiklash — **FAQAT superadmin**, HARD rol tekshiruvi
  (`year-freeze.md` §3 sababi: `AdminPerm` admin'ni ham o'tkazadi); admin 403 + sabab matni oladi.
  Kassa kartalari (`GET /api/admin/cashboxes`) — `finance.main` (ReadRequiresPerm). Tanlov ro'yxati
  (`GET options`) — admin panelidagi har kimga. Yangi ruxsat kaliti YO'Q. Audit turi `Cashbox` →
  bo'lim `finance`.
  ⚠️ **Bu "kassir qoldiqlarni ko'rmaydi" degani EMAS:** `GET /api/admin/finance/transactions` (va
  `summary`/`monthly`) sinf darajasida `[AdminPerm("finance.main")]` — GET har qanday xodimga ochiq
  (kassalardan OLDINGI holat), javobda esa endi `cashboxId/cashboxName` ham bor, ya'ni xom yozuvlardan
  qoldiqni hisoblash mumkin. Yopish — ALOHIDA ish (shu endpointni o'qiydigan sahifalar tekshirilsin).
- **Kassa o'chirilmaydi, arxivlanadi:** yangi pul qabul qilmaydi, qoldig'i va tarixi saqlanadi. Nom
  FAOL kassalar orasida takrorlanmaydi (registrsiz). ⚠️ Qoldig'i bor kassa ham arxivlanadi (rad etilsa,
  minusdagi kassani umuman arxivlab bo'lmasdi): tasdiq oynasi qoldiqni aytadi va uni avval «Kassadan
  kassaga» bilan o'tkazishni taklif qiladi; arxivdagi qoldiqni keyin ham chiqarish mumkin (quyida).
  «Jami» esa ARXIVDAGI kassalar qoldig'ini ham sanaydi (pul jismonan bor).
- **UI:** «Kassalar» tabida kartalar (qoldiq — butun tarix, kirim/chiqim — tanlangan davr); «Kassa»
  tanlovi `PaymentModal`, `TransactionFormModal`, `PaymentEditModal` da FAQAT 2+ faol kassa bo'lsa
  (`hooks/useCashboxOptions.ts`) va o'qituvchi profilidagi «Maosh berish» formasida, oldindan
  foydalanuvchining standart kassasi. Tahrirda yozuvning
  arxivlangan kassasi "(arxiv)" bo'lib turadi — select jimgina boshqa kassaga almashtirmasin.
  «To'lovlar» tabida «Kassa» filtri.
- **Hali YO'Q:** kassa bo'yicha alohida tranzaksiyalar sahifasi, hisobotlarda kassa filtri.
- Testlar: `CashboxServiceTests`, `HomeEndpointsRbacTests` → `Kassalar__yozish_FAQAT_superadmin…`.

## KASSADAN KASSAGA KO'CHIRISH (2026-10-07, jadval `CashboxTransfers`)

Moliya → «Kassalar» → «Kassadan kassaga» (kartadagi ⇄ tugmasi «qayerdan»ni oldindan tanlaydi).
Qoida — `CashboxTransferService`, controller — `CashboxTransfersController`
(`api/admin/cashboxes/transfers`).

⚠️ **ENG MUHIM — ko'chirish KIRIM ham, CHIQIM ham EMAS va `FinanceTransaction` ga YOZILMAYDI.**
Alohida jadval (`CashboxTransfer`) ATAYIN: moliya tranzaksiyalarini 25+ joy o'qiydi (P&L, pul oqimi,
«Kirim chiqim», kassir hisoboti, maosh bazasi, KPI, AI tahlil) — ko'chirish o'sha yerda bo'lsa har biriga
istisno kerak bo'lardi va bittasi unutilsa pul ikki marta "kirim" bo'lib sanalardi. Ko'chirishni FAQAT
`CashboxService.BalancesAsync` / `TotalAsync` o'qiydi: qoldiq = kirim − chiqim + kelgan − ketgan.
Kartada ko'chirish kirim/chiqimga QO'SHILMAYDI — alohida qator (`PeriodTransferIn/Out`).
⚠️ Yangi kassa ko'rsatkichi yozsangiz — qoldiqni QO'LDA yig'mang, `CashboxService` dan oling (aks holda
ko'chirishlar tushib qoladi).

- **Ruxsat — FAQAT superadmin** (HARD rol, kassalardagi bilan bir xil sabab). Kassir O'Z kassasidan
  ham ko'chira olmaydi: ko'chirish = "pul jismonan boshqa kassaga o'tdi" degan da'vo, uni pulni QABUL
  QILGAN tomon (rahbar) yozadi — aks holda kassir kamomadni qog'ozda boshqa kassaga "o'tkazib" yuborardi.
  Tarix (`GET`) — `finance.main` (ReadRequiresPerm). Yangi ruxsat kaliti YO'Q.
- **MINUSGA TUSHMAYDI:** chiqayotgan kassada yetarli qoldiq bo'lmasa — 400 sabab bilan ("qoldiq N,
  ko'chirish M"). Tekshiruv + yozish bitta jarayon qulfi ostida (`LockScope`), bir oyna kaliti
  (`RequestId`) bilan qayta bosilgan "Saqlash" ikkinchi ko'chirish yozmaydi. ⚠️ Oddiy CHIQIM uchun bu
  qoida YO'Q (avvalgidek kassa xarajat bilan minusga tushishi mumkin) — u alohida qaror.
- **Arxivlangan kassa** pul QABUL QILMAYDI, lekin undan CHIQARISH mumkin — arxivdagi qoldiq shu yo'l
  bilan faol kassaga olinadi. O'z-o'ziga, nol/manfiy summa, kelajak sana — rad etiladi.
- **Bekor qilish** (`DELETE {id}?reason=`) — qator O'CHMAYDI (`IsVoided`, global filtr, tarixda ustiga
  chizilgan), ikkala qoldiq tiklanadi. ⚠️ Qabul qilgan kassa pulni allaqachon ishlatgan bo'lsa (qoldig'i
  summadan kam) — RAD etiladi: bekor qilish uni minusga tushirmasin. Audit: `CashboxTransfer` → `finance`.
- **Sana** — pul haqiqatan o'tgan kun (orqaga mumkin, kelajak yo'q). Minus tekshiruvi esa HOZIRGI
  qoldiq bo'yicha (orqaga sanalganda o'tmishdagi oraliq qoldiq tekshirilmaydi — qabul qilingan).
- Testlar: `CashboxTransferTests` (qoldiq, hisobotlarga tushmasligi, minus, bekor qilish, RBAC).

## KIRIM/CHIQIM TURLARI — majburiy toifalar (2026-10-07)

Yig'ilish talabi: to'lov turlari — **O'qish to'lovi · Kitob/qo'llanma sotuvi · Mock imtihon to'lovi ·
Boshqa kirim**; filial xarajatlari ALOHIDA — **Ijara · Kommunal · Internet · Kantselyariya**.

- Yangi TIZIM toifalari (`FinanceCategories`): kirim `book`, `mock`; chiqim `internet`, `stationery`
  (`supplies` — «Jihoz/materiallar» — alohida qoladi). Klient ro'yxati `config/constants.ts`, server
  qulfi `TransactionTypeCatalogTests.KnownCategories` — ikkalasi bir xil bo'lsin. Hisobotlar (P&L, pul
  oqimi, «Kirim chiqim», «Toifalar bo'yicha») allaqachon TOIFA bo'yicha guruhlaydi — yangi toifalar
  o'z-o'zidan alohida qator bo'ladi (yangi hisob-kitob yo'q).
- **Seed BIR MARTALIK** (`FinanceTypeSeed`, `SeedMarker` kaliti `finance-types-2026-10`): mavjud
  bazada bir marta ishlaydi, keyin admin o'chirgan tur QAYTA tug'ilmaydi. Eski turlar QAYTA BOG'LANADI
  (yangisi qo'shilmaydi): «Kitob uchun»/«Mock to'lov» (`other` edi), «Internet va telefon» (`utilities`
  edi), «Kanstovar» (`supplies` edi). Faqat ESKI toifadagi qator ko'chiriladi; nomdosh tur boshqa
  toifada tursa — tegilmaydi (admin qarori). Yetishmagani bo'lim oxiriga qo'shiladi.
- ⚠️ **Mavjud TRANZAKSIYALAR qayta yozilmaydi** (backfill YO'Q): eski «Internet» xarajatlari hisobotda
  «Kommunal»da qoladi, yangilari — «Internet»da.
- **Kitob puli endi kassaga tushadi** (`BookFinance`, `books.md` §7): naqd/karta sotuv tasdiqlanganda
  va nasiya to'langanda — kirim `book`; qaytarishda pul berilsa — chiqim `book` (sotuv kassasidan).
  ⚠️ `tuition` EMAS va `StudentId` YOZILMAYDI — o'quvchi balansi, o'qituvchi foizi, KPI kitob pulini
  ko'rmaydi. `FinanceTransaction.BookOrderId` bor qator Moliyada TAHRIRLANMAYDI va BEKOR QILINMAYDI (400) —
  summasi kitob bo'limida (qaytarish) o'zgaradi; jadvalda «KITOB» belgisi. Moliya → «Yangi amal»da
  kitob turi tanlansa — "bo'limdagi sotuvni qayta kiritmang" ogohlantirishi (pul ikki marta sanalmasin).
- **Mock** — alohida modul YO'Q: kirim qo'lda, «Mock imtihon to'lovi» turi bilan kiritiladi.
- **«Kassalar balansi»** — bosh sahifada FAQAT superadmin uchun ixcham qator (har faol kassa + jami,
  arxivdagilar alohida), Moliya → «Kassalar» ga havola (`CashboxBalanceStrip`).
- Testlar: `FinanceTypeSeedTests`, `BookFinanceTests`, `TransactionTypeCatalogTests`.

## 2026-10-07 AUDIT TUZATISHLARI (T3 — to'lovlar, oylik hisob, kassa)

### AVANS HISOBI — faqat accrual YOZADIGAN oyga (`EnsureChargeAsync`)

To'lov (`PaymentIntake`), to'lovni tahrirlash (`UpdatePayment`) va kelmagan darslar chegirmasi hisob
ochishi mumkin — endi AYNAN `AccrueMonth` shartida: a'zolik `active` + `IsActive` va oy `AccruableMonth`
(aktivlashtirish oyidan KEYIN, muzlatish oyidan OLDIN), o'quvchi arxivlanmagan. Guruhsiz (null) hisob —
faqat a'zoligi UMUMAN yo'q eski o'quvchiga, qabul oyidan boshlab. Shart bajarilmasa hisob OCHILMAYDI,
pul guruh+oy tegi bilan **AVANS** bo'lib balansda turadi (`StudentLedger` yetim tegni FIFO hovuzga oladi).
⚠️ Ilgari holatga qaralmasdi: sinovdagi o'quvchiga, kech aktivlashtirilgan oyga (qisman hisobi yo'q),
muzlatilgan a'zolikka TO'LIQ oylik yozilardi.

⚠️ **ISTISNO — hisobi YO'Q aktivlashtirish oyi** (2026-10-08): o'quvchi yaratish/importda
"active" a'zolik (`AddMembershipRow`) o'z oyiga hisob yozmaydi va accrual ham bu oyni olmaydi — avans
qoidasi bilan shu oyga to'lov keyingi oyni yopar, ya'ni bitta oy TEKIN qolardi. Endi shu oyga
**QISMAN hisob** ochiladi — `ChargeActivationProrateAsync` ning O'ZI (ActivatedAt dan qolgan darslar,
chegirma bilan; summa `ActivationGrossAsync` da — yagona joy). Qisman summa 0 (oyning oxirgi darsidan
keyin aktivlashtirilgan) — hisob ochilmaydi, pul avans. Kelmagan darslar chegirmasi previewi
(`LessonDiscountService.QuoteAsync`) hisobi yo'q oyda AYNAN shu summadan hisoblaydi
(`TuitionService.ActivationChargePreviewAsync`) — aks holda qisman oyga undan katta chegirma yozilardi.
Oila aktivlashtirish oyini emas, KEYINGI oyni to'lasa ham hisobsiz qolgan aktivlashtirish oyi xuddi shu qisman
summa bilan ochiladi (`EnsureRecentActivationChargeAsync`) — faqat YAQIN oy (joriy yoki o'tgan): eski
tarixga (admin o'chirgan oylarga) orqaga hisob yozilmaydi. ⚠️ 2026-10-09 dan Excel importi bu oyni to'lovni
kutmasdan O'ZI yozadi (pastda «QO'SHILGAN OY», `membership-periods.md` §9).

### SINOVDAGI O'QUVCHI TO'LOVI

`PaymentIntake.ResolveGroup` (sof, testlangan): nomzodlar = sinovdan boshqa barcha a'zoliklar + FAOL sinov
a'zoliklari. Guruh yuborilgan va nomzod bo'lsa — AYNAN o'sha; yuborilgan, lekin o'quvchiniki emas — 400
(jimgina boshqa guruhga tushirilmaydi); yuborilmagan: bitta nomzod — o'sha, 2+ — 400 "tanlang". Sinov
guruhiga to'lov — hisobsiz AVANS, aktivlashtirilganda odatdagidek hisobga o'tadi (balans orqali).
⚠️ Ilgari: faqat sinovdagi o'quvchiga GURUHSIZ to'liq hisob ochilardi (aktivlashtirish uni o'chirmas,
keyingi oy ham hisoblanardi — ikki marta); sinov + bitta eski guruhi bor o'quvchining to'lovi tanlovdan
qat'i nazar eski guruhga tushardi. Klient `PaymentModal` faol sinov a'zoligini «— sinovda (avans)» bilan
ko'rsatadi (oylar: joriy + 3, summa qo'lda).

### AVANS QAYTARILSA — kelgusi oy hisobi ham qaytadi (`DropOrphanAdvanceChargeAsync`)

KELAJAK oy hisobini faqat avans ochadi (accrual kelajakni yozmaydi). Shu oyga TIRIK to'lov qolmasa —
to'lov bekor qilinganda, to'liq vozvrat qilinganda yoki tahrirda boshqa oy/guruhga ko'chirilganda —
hisob o'chiriladi va effektiv balansga qaytadi. Joriy/o'tgan oylar, `Locked` qatorlar va boshqa to'lovi
qolgan oy tegilmaydi. Oy kelganda accrual uni odatdagidek yozadi. ⚠️ Ilgari yolg'on qarz + to'lov
eslatmasi SMS'i qolardi.

### BO'LINGAN TO'LOV (`FinanceTransaction.SplitFromId`, `PaymentSplits`)

Guruh almashtirishda (`CarryGroupAdvanceAsync`) to'lov ikkiga bo'linsa yangi qator ASL to'lovga
(oila ildiziga) bog'lanadi va kassir (`CreatedById`), kvitansiya, karta, vaqt, usul, kassa, tur
maydonlarini oladi. **Qaror:** bekor qilish — butun oila birga (jismonan bitta to'lov); vozvrat — har
qatordan ALOHIDA (har qism o'z guruhi/oyiga tegishli), asl qatordan oshiqcha so'ralsa xabar ko'chirilgan
qatorni ko'rsatadi. Kvitansiya nazorati tahrirda oilani dublikat hisoblamaydi. Klient bekor qilish
oynasida "ko'chirilgan qismi ham birga bekor qilinadi" deb ogohlantiradi.

### TAHRIR — ikki yo'l, ikki xil qoida (`FinanceTxEdit`)

| Qator | Qayerdan | Qoida |
|---|---|---|
| O'quvchi to'lovi (kirim + tuition + o'quvchi) | FAQAT «To'lovlar» tahriri (`PUT payments/{id}`) — **serverda ham FAQAT superadmin** (hard rol, 403) | summa allaqachon qaytarilganidan kam bo'lmaydi (400) |
| Vozvrat (chiqim + refund) | «Amallar» tahriri | faqat sana/izoh/sabab; summa, tur, o'quvchi, kassa — 400 |
| Qolganlari | «Amallar» tahriri | forma yubormagan o'quvchi/o'qituvchi/tur toifa o'zgarmasa SAQLANADI |

Umumiy tahrir oddiy qatorni vozvratga yoki o'quvchi to'loviga AYLANTIRA olmaydi; `POST transactions` ham
vozvrat yozmaydi. DTO endi `TypeId` (va `SplitFromId`) qaytaradi — forma turni qaytarib yuboradi.
⚠️ Ilgari «Amallar» dagi vozvrat tahriri `StudentId` ni bo'shatib, qaytarilgan pulni balansga QAYTARIB
berardi (kassadan esa chiqib ketgan), turini "Arenda"ga almashtirardi. Klient: «Amallar» qalami o'quvchi
to'lovida superadmin'ga `PaymentEditModal` ni ochadi (boshqalarga ko'rinmaydi), vozvratda forma faqat
sana/izoh bilan ochiladi. «Yangi moliyaviy amal» → o'quvchi to'lovi formadagi GURUH, SANA va IZOHni
(`comment`) uzatadi (ilgari `undefined` ketardi — 6ba9d616 ishlamasdi).

### OY FORMATI — AYNAN "yyyy-MM" (`PaymentFields.IsMonth`)

To'lov, to'lov tahriri, kirim/chiqim (`Month`), `EnsureChargeAsync`, `POST finance/accrue`, tushum rejasi
va `MonthRange` buzuq oyni qabul qilmaydi (400 / bo'sh oraliq). `POST accrue?month=` KELAJAK oyni rad
etadi; javobdagi `count`/`total` — shu bosishda YANGI yaratilgan hisoblar.

### FON ACCRUAL va BALANS POYGASI

`AccrueMonth` o'quvchilarni faqat REJA uchun (AsNoTracking) o'qiydi; har o'quvchining hisobi uning QULFI
ostida (`PaymentIdempotency.LockAsync("student:…")`, to'lov/vozvrat bilan bir navbat), balans bazadan
QAYTA o'qilib, ALOHIDA saqlanadi (`PurgeDuplicateAggregateChargesAsync` ham). ⚠️ Ilgari fon xizmati
ro'yxatni bir marta yuklab, oxirida eski balans ustidan yozardi — shu orada kelgan to'lov jimgina
yo'qolardi.

**2026-10-08 (audit R1-6, R1-7):**
- **Qulf ICHIDA qayta tekshiruv** — `already` (oy uchun mavjud hisoblar) qulfdan tashqarida bir marta
  o'qiladi; qulf ichida shu o'quvchining shu oydagi hisoblari QAYTA o'qiladi (parallel to'lov/aktivlashtirish
  yozgan bo'lsa — o'tkaziladi).
- **Bitta o'quvchining xatosi yugurishni TO'XTATMAYDI** — `DbUpdateException` (unikal indeks va h.k.) da shu
  o'quvchining yozuvlari kontekstdan ajratiladi, logga yoziladi (`AccrueDue(db, logger)`), keyingisiga
  o'tiladi. Ilgari istisno butun yugurishni uzar: saqlanganlar uchun "oylik hisoblandi" SMS'i ketmas,
  qolganlari 12 soat kutardi. `Count`/`Total`/`Created` — faqat saqlanganlar.
- **Kesh bump'lari to'planadi** — fon xizmati `AccrueDue` ni `DataCache.BeginBatch()` ichida chaqiradi.
- ⚠️ **BALANS QAYTA O'QISH — FAQAT `StudentBalanceSync.ReloadAsync`** (qulf OLMAYDI, chaqiruvchi qulf ichida).
  Kuzatilayotgan o'quvchida `s.Balance = yangi` YETMAYDI: EF birinchi yuklangan snapshot'ni eslaydi va
  "yangi + ta'sir" tasodifan o'sha snapshot'ga teng chiqsa UPDATE YUBORMAYDI (to'lov jimgina yo'qoladi).
  Helper asl qiymatni ham tenglashtiradi. Ishlatiladi: `PaymentIntake`, `AccrueMonth`,
  `PurgeDuplicateAggregateChargesAsync`, `StudentsController.Archive/Restore`, `ClassesController.AddMember`
  (eski arxiv) / `RemoveMember`, `ArchiveController` (tranzaksiya tiklash). Yangi pul yo'lida ham SHU.

### Ko'rsatkichlar

- «Tushum rejasi» → «Shu oyda to'lagan summa» VOZVRATNI ayiradi (`IncomePlan`: `refund` toifasi ham).
- Moliya → «O'quvchilar qarzi» — haqiqiy debitorlik (arxivdagilar ham) qoladi, lekin kartada
  "Faol · Arxivdagi" bo'linishi chiqadi (`FinanceSummaryDto.StudentDebtArchived`) — bosh sahifa faqat
  faollarni sanaydi, ikki sahifa jimgina farq qilmasin.
- Klient «bugun / joriy oy» MAHALLIY sana (`lib/localDate.ts`, `lib/month.ts`): kassirning «To'lovlarim»
  strelkalari va "7 kun" (UTC+5 da bir kun surilardi), to'lov/vozvrat/tahrir oynalari.

Testlar: `AuditFixT3Tests`, `AuditFixFx2Tests`.

## 2026-10-09 — MIJOZ SHIKOYATLARI (balans)

### QO'SHILGAN OY — import darhol hisoblaydi + bazadagilarni tuzatish (`ActivationChargeRepair`)

Shikoyat: "shablonga «aktiv» qilib qo'shilganda qarzdorlik hisoblanmay, balans 0 bo'lib ochilyapti". Import
endi «active» a'zolikning qo'shilgan oyiga `ChargeActivationProrateAsync` (Aktivlashtirish oynasidagi AYNAN o'sha
qisman summa) yozadi; natija oynasida "N ta o'quvchiga jami X qarz yozildi" (`StudentImportResultDto.JoinMonthCharged`).

Bazada qolib ketganlar — **Moliya → «Qo'shilgan oy hisobini tuzatish»** (FAQAT superadmin, HARD rol;
`BillingRepairController` `api/admin/billing-repair/activation-charges`):
- `GET` — nomzodlar ro'yxati, **hech narsa yozmaydi**; `POST {keys}` — faqat BELGILANGANLAR, ro'yxat serverda
  QAYTA quriladi (klient summasiga ishonilmaydi), har o'quvchi o'z qulfi ostida ALOHIDA saqlanadi.
- Nomzod (`ActivationChargeRepair.IsCandidate`, sof): `active` + tirik, aktivlashtirish oyi JORIY yoki O'TGAN
  (`EnsureRecentActivationChargeAsync` chegarasi — eski tarixga orqaga qarz yo'q), o'sha oyda muzlatilmagan, shu
  oyda yopilgan oldingi davri yo'q, (o'quvchi, guruh, oy) uchun hisob qatori UMUMAN YO'Q (0 so'mlik/qulfli qator
  ham "bor"), o'quvchi va guruh arxivda emas, qisman summa > 0.
- Avto-xabar yuborilmaydi (tuzatish, yangi oy emas). Audit — `Membership` («Qo'shilgan oy hisobi tuzatildi …»).
- Testlar: `ActivationBillingFixTests` (`Tuzatish__*`).

### AKTIVLASHTIRISH SANASINI O'ZGARTIRISH (`ActivationDateChange`)

Shikoyat: 8-sanadan qo'shilib −180 000 hisoblangan o'quvchi 225 000 to'lasa +45 000 qolardi — sanani orqaga
surish yo'li yo'q edi. Profil → guruh kartasi «⋮» → **«Aktivlashtirish sanasini o'zgartirish»**
(`POST classes/{id}/members/{sid}/activation-date {date, preview}`, ruxsat — aktivlashtirish bilan bir xil).
- Yangi sananing oyi — `TuitionService.RechargeActivationMonthAsync` (qisman qayta hisob; dars qolmasa oy
  hisobi olib tashlanadi). OLDINGI oyga surilsa — eski aktivlashtirish oyi `RechargeFullMonthAsync` bilan TO'LIQ,
  oraliq oylar `AccrueCatchUpAsync` bilan darhol.
- Cheklovlar (`BlockReason`, sof): faqat `active`; kelajak yo'q; KEYINGI oyga surish yo'q (o'tgan oy hisoblarini
  bekor qilish kerak bo'lardi — muzlatish/chiqarish bor); 12 oydan ortiq orqaga yo'q; yopilgan oldingi davrga
  tegmaydi. Qo'lda tahrirlangan (`Locked`) oy TEGILMAYDI va javobda `lockedMonths` bo'lib aytiladi.
- UI ikki qadam: «Hisoblash» (`preview` — server hisoblaydi, `ChangeTracker.Clear()`, hech narsa saqlanmaydi) →
  qaysi oy qanchaga o'zgarishi va yangi balans → «Saqlash». Audit — `Membership`, har oy "eski → yangi".
- Testlar: `ActivationBillingFixTests` (`Sana_*`, `Qolda_tahrirlangan_*`).

### BALANSNI TUZATISH (profil «⋮», FAQAT superadmin)

Shikoyat: bitta guruhda har xil tarifda o'qiydiganlar (koreys tili: Topik 1 va Grammatika) — balansni qo'lda
to'g'rilash kerak. ⚠️ Balans TO'G'RIDAN-TO'G'RI yozilmaydi: u hisoblar va to'lovlardan kelib chiqadi (per-guruh
balans, maosh bazasi `MonthlyCharge.Amount`, qarzdorlar) — alohida "tuzatish" yozuvi ularning hammasiga istisno
talab qilardi. Shuning uchun oyna mavjud **qo'lda oylik tahririni** (`EditCharge`, `discounts.md` §8.5) ochadi:
oy + guruh tanlanadi, yangi hisob summasi va IZOH (`Note`, tarixga) kiritiladi, «Hisoblash» (`Preview`) yangi
balansni SERVERDAN ko'rsatadi (chegirma yangi summadan qayta hisoblanadi), keyin «Saqlash». Tuzatilgan oy
qulflanadi; KEYINGI oylar guruh narxida yoziladi (shaxsiy narx — alohida ish, hali YO'Q).
- ⚠️ `EditCharge` endi SERVERDA ham faqat superadmin (403 sabab bilan; ilgari `students.list:edit` yetardi) va
  o'quvchi qulfi ostida, balans `StudentBalanceSync.ReloadAsync` bilan qayta o'qiladi.
