---
description: Test natijalari (FAQAT natija — turi + savollar soni + yechgani; Excel'dek jadval, ota-onalarga yuborish; onlayn test 2026-10 da olib tashlandi) va TEST SERTIFIKATI (Word andoza → PDF).
paths:
  - "WunderkindLC.Application/Services/TestResultService.cs"
  - "WunderkindLC.Application/Services/TestResultService.Entry.cs"
  - "WunderkindLC.Application/Services/TestEntryRules.cs"
  - "WunderkindLC.Application/Services/TestScoreMessages.cs"
  - "WunderkindLC.Server/Controllers/TestResultsController.Grid.cs"
  - "WunderkindLC.Server/Controllers/TeacherPortalController.Tests.cs"
  - "WunderkindLC.Client/src/pages/teacher/tests/**"
  - "WunderkindLC.Client/src/lib/testScoreGrid.ts"
  - "WunderkindLC.Application/Services/TestCertificateService.cs"
  - "WunderkindLC.Application/Services/DocxToPdfConverter.cs"
  - "WunderkindLC.Application/Services/DocxTemplate.cs"
  - "WunderkindLC.Application/Services/LevelTestService.cs"
  - "WunderkindLC.Server/Controllers/TestResultsController.cs"
  - "WunderkindLC.Server/Controllers/LevelTestsController.cs"
  - "WunderkindLC.Server/Controllers/PublicTestController.cs"
  - "WunderkindLC.Client/src/pages/admin/tests/**"
  - "WunderkindLC.Client/src/pages/admin/level-tests/**"
---

# Test natijalari qoidalari

## 0. CRM U4a (2026-10-12) — FAQAT NATIJA: turi + savollar soni + YECHGANI

SPEC: `docs/SPEC-crm-upgrade.md` P4.1–P4.2; egasining qarori (`docs/ASSUMPTIONS.md` 2026-10-11): savol tuzish/tarqatish
YO'Q — guruh bo'yicha faqat NATIJA: nom, sana, **TURI** (`monthly` «Oylik» / `interim` «Oraliq», yaratishda SHART),
**«Savollar soni»** (1..200, yaratishda SHART) va har o'quvchiga BITTA raqam — nechta savolni YECHGANI. Foiz =
yechgan ÷ savollar × 100 (ikkita mustaqil kiritish ziddiyatga olib kelardi). Oyiga 1–2 marta.

- **Model:** `TestResult.Kind` · `ResultsSentAt` · `ResultsSentById` (`Domain/Crm/Rating.cs`). Mavjud `QuestionCount`
  ustuni «Savollar soni» sifatida qayta ishlatiladi va yangi testda **`MaxScore = QuestionCount`**, ball = yechgan
  soni — ya'ni reyting dvigateli (`RatingFormula.Percent(score, max)`) O'ZGARMAYDI (`rating.md` §3).
  ⚠️ ESKI testlar (`QuestionCount = 0`) maxraj sifatida `MaxScore` ni saqlaydi: jadvalda ustun «Ball», oraliq
  0..⌊MaxScore⌋ (baribir BUTUN son). Migratsiyada hamma eski test `interim` (`docs/migrations/Phase4a.sql`).
- **Qoidalar YAGONA joyda** — `TestEntryRules` (sof, `TestScoreBatchTests`), servis — `TestResultService.Entry.cs`
  (`CreateEntryAsync`, `UpdateEntryAsync`, `DeleteEntryAsync`, `OverviewAsync`, `SaveScoresBatchAsync`). Admin ham,
  o'qituvchi ham AYNAN shularni chaqiradi (eski `CreateAsync`/`UpdateDetailedAsync`/`SetScoreAsync` faqat moslik uchun
  qoldi — eski bitta-katak `PUT {id}/scores` hamon QISADI).
- ⚠️ **NOTO'G'RI QIYMAT RAD ETILADI, QISILMAYDI** (`PUT …/{id}/scores/batch` `{ rows: [{ studentId, solved }] }` →
  `{ detail, errors }`): to'g'ri qatorlar saqlanadi, 0..savollar oralig'idan tashqari yoki kasr — «Natija 0 dan {n}
  gacha butun son bo'lsin», begona o'quvchi — «O'quvchi bu guruh a'zosi emas». `null` — tozalash. Qabul qilinadi:
  hozirgi faol (muzlatilmagan) a'zo YOKI shu testda natijasi bor o'quvchi (chiqib ketganning natijasini tuzatish/
  tozalash mumkin). Ilgari 31/30 jimgina 30 bo'lib saqlanardi — o'qituvchi xatoni hech qachon ko'rmasdi.
- ⚠️ **«Savollar soni» KAMAYTIRILSA** va kimningdir natijasi undan katta bo'lsa — 400 «{n} ta o'quvchining natijasi
  yangi savollar sonidan katta» (qisish YO'Q). Eski testda `MaxScore` tahriri avvalgidek (pastdagi «QISILADI» bandi).
- Tafsilot qatorlari `percent` (bir kasr) va `rank` (musobaqa usuli "1, 2, 2, 4") bilan; ro'yxatlar `kind`,
  `questionCount`, `avgPercent`, `resultsSentAt` bilan.
- **Oy/tur filtri:** `GET api/admin/test-results?groupId=&month=yyyy-MM&kind=`, `GET …/groups?month=` (`monthTestCount`,
  `monthAvgPercent`), o'qituvchi `GET api/teacher/test-results?classId=|groupId=&month=&kind=`; markaz kesimi —
  `GET api/admin/test-results/overview?month=&kind=&courseId=&teacherId=`. Noto'g'ri `month`/`kind` — filtrsiz
  (eskirgan havola ro'yxatni bo'shatmasin). Sanalar QAT'IY `yyyy-MM-dd` (filtr satr solishtiradi) — boshqasi 400.
- **Audit** — tur `TestResult` (bo'lim «Guruhlar», `EntityId = "{groupId}:{testId}"` — guruh «Tarix» tabida):
  yaratish, tahrir (oldin/keyin), o'chirish (nechta natija bilan) va yuborish («Test natijalari ota-onalarga
  yuborildi: {test}, {n} ta»). Har bir ball kiritish YOZILMAYDI (jadvalni to'ldirish tarixni to'ldirib yuborardi).
- **Ruxsat:** admin — sinf darajasidagi `classes.testResults` (GET — ko'rish, batch PUT — tahrir, yuborish POST —
  yaratish); o'qituvchi — `OwnsGroup` (`journal` + guruh EGASI; o'rinbosarga YOPIQ). Yangi kalit YO'Q.
- **Menyu:** «Testlar natijalari» Future'dan asosiy «O'quv bo'limi» guruhiga ko'chdi (`navCrmPhase4a.test.ts`).

### 0.1. Excel'dek jadval — `pages/admin/tests/TestScoreGrid.tsx` (admin VA o'qituvchi — bitta komponent)

- Qatorlar ISM bo'yicha va yozish paytida QAYTA SARALANMAYDI; ustunlar «№», «F.I.Sh», «Yechgan (0–30)» (eski testda
  «Ball»), «Foiz» (jonli), «O'rin» (oxirgi saqlashdan). `NumberInput` (butun son, `inputMode` raqamli, mobil'da 16px).
- Enter/↓ — keyingi qator, Shift+Enter/↑ — oldingisi. Excel ustunini katakka qo'yish — shu qatordan PASTGA to'ldiradi
  (har qatorning 1-ustuni, `"25/30"` → 25, bo'sh — tozalaydi, tanib bo'lmagan matn — qizil «Raqam emas: …»).
  Sof qism — `lib/testScoreGrid.ts` (`parsePastedColumn`, `nextRowIndex`, `cellError`; testlari bor).
- Avtosaqlash — oxirgi tahrirdan **800 ms** keyin YOKI jadvaldan TASHQARIGA chiqilganda (qatorlar orasida yurish
  so'rov yubormaydi) — BITTA batch. Holat: «Saqlanmoqda…» / «Saqlandi» / qizil xato (qiymatlar joyida qoladi).
- Noto'g'ri katak QIZIL (sababi `title`da) va YUBORILMAYDI; server rad etgani ham qizil qoladi.
- ⚠️ ESKI testdagi KASRLI ball (37.5) faqat «✎» bosilganda tahrirlanadi — aks holda `NumberInput` uni yaxlitlab
  JIMGINA qayta saqlab yuborardi (`number-inputs.md` §1).
- Sertifikat yoki yuborishdan OLDIN jadval `flush()` qilinadi (ref) — saqlanmagan natija chetda qolmasin.

### 0.2. «Ota-onalarga yuborish» — `TestScoreMessages` (`POST …/{id}/send-results` `{ resend? }`)

- Faqat QO'LDA, har test uchun (egasining qarori: har saqlashda xabar — o'zgaruvchan o'rinlar bilan spam bo'lardi).
  Trigger `test_score`, faqat OUTBOX orqali (`messaging.md` §0.5). Yoqilgan qoida yo'q — 400 «"Test natijasi
  (ota-onaga)" avto-xabar qoidasi yoqilmagan — Xabarlar → Avto xabarlar».
- Tokenlar: {test}, {turi}, {sana}, {ball} "25/30", {foiz} "83%", {orin} "3/18" (testdagi o'rin / natija kiritilganlar),
  {guruh_orin} va {reyting_orin} — test OYINING reytingidan (`RatingEngine`, keshlangan; o'rinsiz — «—»), {guruh}.
- ⚠️ **Faqat O'ZGARGAN natija qayta ketadi:** `eventKey = "{testId}:{score}"`; o'quvchining HAR yetadigan chatida shu
  kalitli qator bo'lsa u DISPATCHGA ham chiqmaydi (`skippedUnchanged`) — kabinet qo'ng'iroqchasi ham takrorlanmaydi.
  Yangi ulangan ota-ona chati — yangi qator (faqat o'shanga). `resend = true` → `":r{ticks}"`, hammaga qayta.
- Arxivdagi o'quvchi — yo'q. Yetadigan chati yo'q (bloklagan ham) — `noTelegram`, dispatch qilinmaydi.
- Biror xabar navbatga tushsagina `ResultsSentAt/ById` yangilanadi va audit yoziladi (UI: «Yuborilgan: 12.10 15:40»).
  Javob: `{ queued, noTelegram, skippedUnchanged }`.

- **Test natijalari** (migratsiya `AddTestResults`): o'qituvchi o'quvchilardan olgan testlar ballarini
  kiritadi. Entity: `TestResult`(GroupId, Name, Date, MaxScore, CreatedAt, CreatedBy) +
  `TestScore`(TestResultId, StudentId, Score) — per (test, o'quvchi) unikal, test o'chsa ballari FK
  CASCADE. Mantiq YAGONA: `TestResultService` (Application/Services) — `GroupsOverviewAsync` (barcha
  guruh + testlar soni), `ListForGroupAsync`, `CreateAsync/UpdateAsync/DeleteAsync`, `DetailAsync`
  (guruhning FAOL a'zolari `StudentGroup.IsActive` + ballari, **ball desc bo'yicha saralangan**, ball
  kiritilmaganlar oxirida Rank=0), `SetScoreAsync` (0..MaxScore ga clamp, null → tozalash, qaytadi:
  qayta saralangan tafsilot), `StudentResultsAsync` (o'quvchi profili uchun).
  Admin: `TestResultsController` (`api/admin/test-results`, AdminPerm "classes") — `/groups`,
  `?groupId=`, `POST/PUT/DELETE`, `/{id}` detail, `/{id}/scores`, `/student/{sid}`. O'qituvchi:
  `TeacherPortalController`da `test-results/*` (faqat `Group.TeacherId==me` — `OwnsGroup`/`GroupIdOfAsync`).
  Frontend: **admin** "O'quv bo'limi → Testlar natijalari" (`pages/admin/tests/`: `TestResultsPage`
  guruhlar gridi → `TestGroupPage` guruh testlari + yaratish modali → `TestDetailPage` o'quvchilar
  ballari, blur'da avto-saqlanadi va qayta saralanadi, TOP-3 medal), nav "O'quv bo'limi" ostida
  (perm 'classes'); **o'qituvchi** ilovasida pastki navigatsiyadagi "Test" tabi (`/teacher/tests`,
  `TeacherTestsPage` — guruh→test→ballar drill-down); **o'quvchi** admin profilida
  (`StudentDetailPage`) "Testlar natijalari" bo'limi (ball/maks + o'rin).

- **HAR ROLDA BIR XIL — YAGONA PANEL:** test ro'yxati/yaratish/tahrir/o'chirish har bir ilovada
  BITTA komponentda, u ikki joyda qayta ishlatiladi (nusxa YO'Q):
  • admin — `pages/admin/tests/GroupTestsPanel.tsx` (`GroupTestsPanel` + ichida `TestFormModal`):
    `TestGroupPage` va guruh (jurnal) sahifasidagi "Imtihonlar" tabi (`ClassDetailPage`);
  • o'qituvchi — `pages/teacher/tests/TeacherGroupTestsPanel.tsx` (`TeacherGroupTestsPanel` +
    `TeacherTestFormModal`): `TeacherTestsPage` va jurnal sahifasidagi "Imtihonlar" tabi
    (`TeacherGroupDetailPage`).
  Ya'ni **jurnalning ichida ham test yaratiladi** (2026-10 dan faqat oflayn). Ruxsat: superadmin/admin cheklovsiz, xodim `classes.testResults` (`usePerm` + `[AdminPerm("classes.testResults")]`),
  o'qituvchi `journal` + `OwnsGroup` — hammasi bir xil `TestResultService`ga boradi.

- **2026-10: ONLAYN TEST REJIMI OLIB TASHLANDI** (egasining qarori, `docs/ASSUMPTIONS.md` 2026-10-11).
  Testlar FAQAT oflayn — ballni o'qituvchi/admin qo'yadi. Kod bilan birga ketdi: `OnlineTestBotService`
  (bot PDF + javob kiritish), `OnlineTestService` (o'quvchi kabinetidagi onlayn test, `GET/POST
  /api/student/online-tests`), test KODI va markazdan tashqari ishtirokchilar, o'qituvchining
  `POST /api/teacher/test-results/uploads`, formalardagi rejim/PDF/kalit/vaqt oynasi maydonlari,
  `TestResultService` dagi `ApplyOnline*`/`Regrade`/`NewCodeAsync`/`FindByCode`, `GroupTestDto`
  ning onlayn maydonlari va `TestResultDetailDto.ExternalRows`.
  • **Bot:** «📝 Testni ishlash» tugmasi klaviaturalardan olib tashlandi; eski tugma, `/test` buyrug'i,
    eski inline tugmalar (`ot:`, `oa:`, `og:`, `ocode`…) va `testcode` rejimidagi chat — tushuntirish matni
    + yangilangan klaviatura oladi (`TelegramBotService.IsLegacyOnlineTestCallback`).
  • **BAZA TEGILMAGAN:** `TestResult` ning onlayn ustunlari (`Mode` … `GroupOpen`), `TestScore.Answers/
    SubmittedAt/Source`, `TestBotSessions`, `ExternalTestScores` — legacy (`[Obsolete]` entity'lar),
    tasdiqlangan o'chirishgacha turadi. Guruh o'chirilganda eski qatorlar va savollar PDF'i tozalanadi
    (`GroupDeletion`, quyidagi naqsh qoidasi bilan).
  • Daraja testi (lidlar, `LevelTestService`) va o'quv dasturi testlari bunga KIRMAYDI — ular qoldi.

- **MAKSIMAL BALL KAMAYSA ballar QISILADI** — FAQAT ESKI (savollar sonisiz) testda (`UpdateEntryAsync` /
  `UpdateDetailedAsync` → `ClampScore`, `[0, MaxScore]`; savollar sonili testda — rad, §0): ilgari "9/5 = 180%" chiqardi (sertifikatda ham). Ball o'zgargan bo'lsa (`Regraded > 0`)
  va test bo'yicha sertifikat berilgan bo'lsa — sertifikatlar fonda QAYTA yaratiladi
  (`TestCertificateJobs`, idempotent).

- ⚠️ **ESKI `PdfUrl` — o'qilmaydi, faqat tozalanadi.** 2026-10-08 (KRITIK) hodisa: bot `PdfUrl` ni
  tekshiruvsiz o'qib Telegram hujjati qilib yuborardi (`"/../.env"`). Bot oqimi endi YO'Q va servis
  `PdfUrl` ni YOZMAYDI (`SecurityFixTests` qulflaydi). Guruh o'chirilganda savollar PDF'i faqat server
  naqshida (`UploadUrlRules.IsServerPdf`) bo'lsa VA boshqa test, shartnoma yoki o'quv dasturi materiali
  (`CourseItem.PdfUrl`) uni ishlatmasa o'chiriladi; sertifikat fayllari — faqat
  `/uploads/certificates/cert-<guid>.docx|pdf`.

## TEST SERTIFIKATI — Word andoza → PDF (migratsiya `AddTestCertificates`)

Test natijasi bo'yicha o'quvchiga sertifikat beriladi. Andoza — **Word (.docx)**, admin yuklaydi;
PDF ga o'girish **LibreOffice headless** orqali (ko'rinish AYNAN saqlanadi).

- **Entitylar:** `TestCertificateTemplate` (Name, FileUrl `/uploads/*.docx`, IsDefault, IsActive) va
  `TestCertificate` (kalit **(TestResultId, StudentId) UNIKAL** — bir test bo'yicha o'quvchiga bitta;
  Number `SRT-yyyy-NNNN`, DocxUrl, PdfUrl, Status, ball/foiz SNAPSHOT). `TestResult` ga
  `CertificateEnabled` (test formasidagi ptichka) + `CertificateTemplateId` qo'shildi.
  ⚠️ Bu mavjud `StudentCertificate` (kursni TUGATGANLIK, HTML) dan ALOHIDA: u yerda kalit
  (o'quvchi, kurs, sana) bo'lib, bir kunda ikkita test sertifikati to'qnashardi.
- **Tokenlar** — `TestCertificateService.Tokens` **yagona manba**: `@fish @guruh @kurs @oqituvchi
  @test @ball @maksball @foiz @orin @sana @bugun @raqam`. Admin paneli shu ro'yxatni
  `GET /api/admin/test-results/certificate-tokens` dan oladi (qo'lda takrorlanmaydi).
  Almashtirish `DocxTemplate` da — **shartnoma andozalari bilan bir xil kod** (ilgari
  `ContractService` ichida edi, ajratib olindi). Paragraf darajasida ishlaydi, chunki Word bitta
  so'zni bir nechta "run"ga bo'lib yozadi va oddiy `Replace` topa olmaydi.
- **Oqim:** test formasida ptichka + shablon tanlanadi → natijalar kiritiladi → **«Saqlash va
  sertifikat yaratish»** → `POST .../{testId}/certificates` ball kiritilgan HAR o'quvchiga bitta
  sertifikat yaratadi. **IDEMPOTENT**: qayta bosilsa mavjudlari YANGILANADI (ball o'zgargan
  bo'lishi mumkin), raqam saqlanadi, nusxa yaratilmaydi.

### BO'LAKLAB + FONDA yaratish (nega shunday)

LibreOffice narxi **faylga emas, jarayonni ochishga**: sovuq start ~2-4 s va ~150-200 MB, bitta
hujjatni chizish esa ~0.5-1 s. Shundan kelib chiqib **ikkita** qaror qabul qilingan:

- **Bo'laklab** — `TestCertificateService.ChunkSize = 5`. Hammasini bitta chaqiruvda chizish jami
  eng tez edi, lekin 30 o'quvchida barcha `.docx` bir vaqtda xotirada turardi va LibreOffice
  cho'qqisi ~400 MB ga chiqardi — **1 GB RAM li serverda OOM xavfi**. Bittalab chizish esa startni
  30 marta to'lardi (~110 s). 5 tadan — start 6 marta (~40 s), xotira cho'qqisi ~2 barobar past.
  Har bo'lak o'z `SaveChanges`i bilan tugaydi, shuning uchun tayyor sertifikatlar ish tugashini
  kutmasdan ro'yxatda ko'rinadi.
- **Fonda** — `TestCertificateJobs` (Singleton). Ilgari generatsiya so'rov ICHIDA edi va katta
  guruhda **Cloudflare 100 soniyada `524` qaytarardi**. Endi `POST` ishni boshlab darhol qaytadi,
  UI esa `GET .../{id}/certificates/status` ni ~2.5 soniyada bir so'raydi (`TestCertificateJobDto`:
  `running/total/done/items/error/warning` — ro'yxat holat bilan BITTA javobda keladi).
  ⚠️ Tekshiruvlar (shablon yo'q, ball kiritilmagan) **so'rov ichida** bajariladi
  (`ExpectedCountAsync` — hech narsa yaratmaydi), fonga o'tkazilmaydi: xato darhol ko'rinishi kerak.
  ⚠️ Fon ishiga so'rovning `CancellationToken`i **BERILMAYDI** — javob yuborilgach u bekor bo'lib,
  ish yarim yo'lda to'xtab qolardi.
  Holat **xotirada** (baza jadvali emas — ish bir necha daqiqa yashaydi, tugagach 10 daqiqa
  saqlanadi). Server qayta yuklansa holat yo'qoladi: UI `Running=false` ko'radi va bazadagi tayyor
  sertifikatlarni ko'rsatadi — tugmani qayta bosish yetarli (generatsiya idempotent).
  UI: progress chizig'i «Yaratilmoqda... 12/30», tayyor bo'lganlar darhol yuklab olinadi,
  **«Hammasini yuklash (ZIP)» faqat ish tugagach** faollashadi (yarim to'plam "hammasi" bo'lmasin).

- **PDF bo'lmasa ham ishlaydi:** `DocxToPdfConverter` LibreOffice'ni topa olmasa `null` qaytaradi →
  sertifikat `Status="docx"` bilan faqat Word sifatida saqlanadi va UI amber ogohlantirish
  ko'rsatadi. **Server LibreOfficesiz ham ishlaydi — bu ataylab.**
  ⚠️ 1GB RAM: konvertatsiya ~150-200MB oladi, shuning uchun `SemaphoreSlim(1,1)` bilan NAVBAT
  bilan bajariladi (bu bo'laklardan tashqari yana bir himoya: ikki foydalanuvchi bir vaqtda
  yaratsa ham LibreOffice bittadan ishlaydi). Docker image'ga `libreoffice-writer` +
  `fonts-liberation`/`fonts-dejavu` qo'shilgan (shriftsiz o'zbek harflari kvadratga aylanadi),
  `HOME=/tmp` shart.
- **Fayllar `ContentRootPath/uploads/certificates`** ga yoziladi — `wwwroot` ga EMAS. Sabab:
  `/uploads` Program.cs'da ContentRoot dan beriladi va docker volume + tungi zaxiraga kiradi;
  wwwroot esa har deployda qayta yoziladi (eski HTML sertifikatlardagi xato aynan shu).

### ⚠️ `uploads/certificates` — STATIK YO'L BILAN BERILMAYDI

`/uploads` ochiq statik papka: manzilni bilgan har kim **login'siz** oladi. Sertifikat esa shaxsiy
ma'lumot (F.I.Sh, ball, foiz, o'quvchining SURATI) — API'dagi `OwnsGroup`/`AdminPerm` tekshiruvi
faylning o'zini qo'riqlamasdi: manzil bir marta oshkor bo'lsa (brauzer tarixi, ulashilgan havola)
u **abadiy** ishlayverardi, o'qituvchi guruhdan chiqarilsa ham.

Yechim — `PrivateFolderFileProvider` (Application/Services): statik fayl provayderi ustidagi
darvoza. Program.cs'da **ikkala** `UseStaticFiles` ham shu bilan o'raladi, chunki ikki sertifikat
tizimi ikki xil joyga yozadi: yangi test sertifikatlari `ContentRoot/uploads/certificates`,
eski HTML sertifikatlar `wwwroot/uploads/certificates`.

- **Fayllar KO'CHIRILMAYDI** — `uploads` docker volume + tungi zaxiraga kiradigan yagona papka;
  undan chiqarish zaxiradan tushib qolish degani bo'lardi. Fayllar joyida qoladi, faqat
  berilmaydi. Ya'ni **migratsiya ham, mavjud fayllarga tegish ham kerak emas**.
- **Yuklab olish o'zgarmaydi** — u avvalgidek avtorizatsiyalangan endpointlar orqali, fayl
  diskdan o'qib beriladi (`ReadFileAsync`/`ZipForTestAsync`). Mijozga to'g'ridan-to'g'ri fayl
  manzili berilmaydi: eski tizimda `DownloadUrl` har doim API manzili, yangi tizimda UI
  `certificates/{id}/download` ni chaqiradi (DTO'dagi `DocxUrl`/`PdfUrl` — ichki saqlash havolasi,
  frontendda ishlatilmaydi).
- **Tekshiruv MANZILGA emas, hal qilingan FIZIK YO'LGA asoslanadi** — `..` segmentlari yoki
  registr bilan chetlab o'tib bo'lmaydi (jonli tekshirildi: `/uploads/certificates/x.pdf`,
  `.../certificates/../certificates/x.pdf`, `/uploads/CERTIFICATES/x.pdf` — hammasi 404;
  `/uploads/surat.jpg` — 200).
- Rad etilgan har urinish **logga** yoziladi ("Statik yo'l bilan MAXFIY faylga urinish rad etildi")
  — kutilmagan mijoz (masalan eski havola) bo'lsa shu yerdan ko'rinadi.
- `Uploads:PublicCertificates=true` — favqulodda qaytarish kaliti (kodni qayta yig'masdan).

**HALI OCHIQ:** `/uploads` dagi qolgan fayllar — o'quvchi surati, kitob muqovasi, dars PDF —
hamon login'siz olinadi. Ular `<img>`/`<iframe>` da kerak, brauzer esa u yerga `Authorization`
sarlavhasini yubora olmaydi (loyihada JWT Bearer, cookie yo'q). Ularni yopish alohida ish:
login'da `Path=/uploads` cookie qo'yib, papkani cookie/token bilan darvozalash — bunda mobil
ilovalar ham yangilanishi kerak, shuning uchun avval "faqat log yozadigan" rejimda o'lchash tavsiya
etiladi. Hozirgi himoya: manzil tasodifiy GUID + `Referrer-Policy: no-referrer` + `Cache-Control: private`.
- **API:** admin `api/admin/test-results` — `certificate-tokens`, `certificate-templates`
  (GET/POST/PUT/DELETE), `{id}/certificates` (POST yaratish / GET ro'yxat),
  **`{id}/certificates/status`** (fon ishi holati + shu daqiqada tayyor ro'yxat),
  `certificates/{id}/download?format=docx`, `{id}/certificates/download` (ZIP).
  O'qituvchi `api/teacher/test-results` — `certificate-templates` (faqat o'qish),
  `{id}/certificates` (POST), **`{id}/certificates/status`**, download + ZIP.
  ⚠️ **RUXSAT:** o'qituvchi tarafdagi BARCHA test amallari `TeacherPortalController.OwnsGroup`
  orqali o'tadi va u endi IKKI narsani tekshiradi: **`journal` ruxsati** VA guruh egasi ekani.
  Ilgari faqat egalik tekshirilardi — ya'ni admin "Jurnal" ruxsatini olib qo'ysa ham o'qituvchi
  API'ga to'g'ridan-to'g'ri murojaat qilib ball qo'ya olardi, testni o'chira olardi va sertifikat
  yaratardi. Yangi endpoint qo'shilsa — u ham SHU helper orqali o'tishi shart.
  Andozalarni FAQAT admin boshqaradi, o'qituvchi tanlaydi.
- **UI:** admin `/admin/test-results/certificate-templates`
  («Testlar natijalari» sarlavhasidagi «Sertifikat shablonlari» tugmasi): shablonlar CRUD +
  bosilsa nusxalanadigan o'zgaruvchilar jadvali. Test tafsilotida (admin va o'qituvchi) har
  o'quvchi yonida `ball / maks · foiz`, pastda «Saqlash va sertifikat yaratish» va
  «Sertifikatlar» bo'limi (har biri uchun «Yuklab olish» + «Hammasini yuklab olish (ZIP)»).
- Shablon o'chirilmaydi, agar undan sertifikat berilgan bo'lsa — **nofaol** qilinadi (tarix buzilmasin).
  Nofaol shablon `certificateTemplateId` orqali ham ISHLATILMAYDI (`ResolveTemplateAsync` `IsActive`
  talab qiladi) — aks holda testda saqlanib qolgan eski id bilan u baribir ishlatilaverardi.
- **Shablon fayli — SHU BO'LIMNING O'ZINIKI.** Yuklangan fayl `/uploads` ichida `certtpl-<guid>.docx`
  nomi bilan NUSXALANADI va shablon shu nusxaga ishora qiladi; o'chirish ham FAQAT shu prefiksli
  fayllarga qo'llanadi. Sabab: `/uploads` yagona tekis papka (shartnomalar, skanlar ham shu yerda) —
  ilgari shablon istalgan mavjud `.docx` manzilini ko'rsata olardi va o'chirilganda **begona fayl
  diskdan o'chib ketardi**. Eski (nusxalashdan oldingi) shablonlarning fayli tegilmay qoldiriladi.
- **ESKIRGAN sertifikat olib tashlanadi:** ball TOZALANGAN o'quvchining sertifikati qayta yaratishda
  o'chiriladi (yozuv + fayl). Ilgari noto'g'ri qo'yilgan ball sertifikati ro'yxatda ham, ZIP ichida
  ham eski ball bilan abadiy qolib ketardi.

### Sertifikatda O'QUVCHI SURATI

Ikki usul qo'llab-quvvatlanadi — yagona kirish nuqtasi `DocxTemplate.ApplyPhoto`:

1. **`@rasm` matn belgisi** (oddiy yo'l) — shablonda shunchaki `@rasm` deb yoziladi va o'sha joyga
   surat **185×260 px** o'lchamda qo'yiladi (`PhotoWidthPx`/`PhotoHeightPx`; 96 DPI → ×9525 EMU).
   Belgi bir nechta joyda bo'lishi mumkin, kolontitullarda ham ishlaydi. Word uni bir necha
   "run"ga bo'lib yozgan bo'lsa ham topiladi (paragraf matni birlashtirilib, belgilar bo'yicha
   bo'laklarga ajratiladi, rasmlar oralarga qo'yiladi).
   ⚠️ **BITTA paragrafda bir nechta `@rasm`** bo'lsa HAMMASI almashtiriladi (ilgari faqat
   birinchisi almashtirilib, qolgani sertifikatda "@rasm" yozuvi bo'lib chop etilardi).
   ⚠️ Belgidan **keyingi matnning formatlashi saqlanadi** — asl run'ning `RunProperties`
   (shrift/o'lcham/qalinlik/rang) nusxalanadi; aks holda `@rasm @fish` da ism standart shriftga
   tushib qolardi.
2. **Word'dagi rasm o'rni** — muallif rasm qo'yib o'lchami/ramkasi/joyini o'zi sozlaydi, biz faqat
   MAZMUNINI almashtiramiz. Bunda **muallif bergan o'lcham TEGILMAYDI**.

Belgi ishlatilgan bo'lsa 2-yo'lga o'tilmaydi (belgi — aniq ko'rsatma). `Fill` dan KEYIN chaqiriladi
va **surat bo'lmasa ham** chaqirilishi shart: aks holda `@rasm` (noma'lum token sifatida `Fill`
tegmasdan qoldirgan) sertifikatda matn bo'lib qolardi.

- **Qaysi rasm tanlanadi:** nomi/alt-matnida `rasm|surat|foto|photo` bo'lgani; belgi yo'q bo'lsa
  va hujjatda BITTA rasm bo'lsa — o'sha. Aks holda (logotip + rasm, ikkalasi belgilanmagan)
  **hech narsa o'zgartirilmaydi** — noto'g'ri rasmni buzgandan ko'ra tegmagan yaxshi.
- **Surat CHO'ZILMAYDI:** katakni to'ldiradi, ortiqchasi MARKAZDAN qirqiladi (`DocxTemplate.CoverCrop`
  → `a:srcRect`, qiymatlar foizning mingdan biri: 100000 = 100%). Qirqishni **Word/LibreOffice o'zi
  bajaradi** — rasmni qayta chizadigan kutubxona (ImageSharp va h.k.) KERAK EMAS, bu 1GB serverda
  muhim. Surat kvadrat qilib olingani uchun (StudentPhotoDialog) yuz o'rtada qoladi.
  Rasm o'lchami fayl SARLAVHASIDAN o'qiladi (`DocxTemplate.ImageSize` —
  PNG IHDR / JPEG SOFn), tashqi kutubxona SHART EMAS: bizga faqat nisbat kerak.
- **Yangi ImagePart qo'shiladi** va `a:blip/@r:embed` unga yo'naltiriladi — mavjud qismning
  content-type'ini o'zgartirib bo'lmaydi (andozadagi o'rin PNG, surat JPEG bo'lsa baytlarni
  to'g'ridan-to'g'ri yozish hujjatni buzardi). `AddNewPart<ImagePart>(contentType, relId)`
  ishlatiladi — kutubxonaning hujjatlashtirilmagan `ImagePartType` turiga bog'lanmaslik uchun.
- Qo'llanadigan formatlar: jpg/png/gif/bmp/tiff. **webp/heic — o'tkazib yuboriladi** (andozaga
  tegilmaydi). O'quvchi surati `StudentPhotoDialog` dan JPEG bo'lib keladi, muammo yo'q.
  ⚠️ Shu sabab **o'quvchi surati sifatida faqat `.jpg`/`.jpeg`/`.png` qabul qilinadi**
  (`UploadGuard.PhotoExtensions`, `PUT /students/{id}/photo` tekshiradi). Ilgari `/uploads/` bilan
  boshlangan HAR QANDAY fayl (masalan `.pdf` skan yoki iPhone'ning `.heic` surati) avatar bo'lib
  qo'yilaverardi: ro'yxatda buzuq rasm ko'rinardi, sertifikatda esa surat JIMGINA chiqmasdi.
  `.gif/.bmp/.tiff` ham rad etiladi — ularning o'lchamini fayl sarlavhasidan o'qiy olmaymiz,
  ya'ni nisbati saqlanmay cho'zilib ketardi.
- O'quvchida surat bo'lmasa yoki fayl topilmasa — `@rasm` belgisi **olib tashlanadi**, Word'dagi
  rasm o'rni esa **o'z holicha qoladi** (joylashuv buzilmasin). Manba: `Student.BirthCertificateUrl`.
- Admin paneldagi yo'riqnoma matni `TestCertificateService.PhotoHelp` da (yagona manba) va
  `GET certificate-tokens` javobida `photoHelp` bo'lib keladi.
